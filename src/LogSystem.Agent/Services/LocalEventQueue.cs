using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogSystem.Shared.Configuration;
using LogSystem.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogSystem.Agent.Services;

/// <summary>
/// Local encrypted queue for reliable event storage.
/// Events are persisted to disk (AES-256-GCM encrypted) and dequeued on successful upload.
/// Provides crash-resilience — events survive agent restarts.
/// </summary>
public sealed class LocalEventQueue : IDisposable
{
    private readonly ILogger<LocalEventQueue> _logger;
    private readonly SecurityConfig _security;
    private readonly string _queueDir;
    private readonly byte[] _encryptionKey;
    private readonly ConcurrentQueue<string> _pendingFiles = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // In-memory buffers that get flushed to disk periodically
    private readonly ConcurrentBag<FileEvent> _fileEvents = [];
    private readonly ConcurrentBag<NetworkEvent> _networkEvents = [];
    private readonly ConcurrentBag<AppUsageEvent> _appEvents = [];
    private readonly ConcurrentBag<AlertEvent> _alerts = [];

    public LocalEventQueue(
        ILogger<LocalEventQueue> logger,
        IOptions<AgentConfiguration> config)
    {
        _logger = logger;
        _security = config.Value.Security;
        _queueDir = _security.LocalQueuePath;

        // Derive encryption key from device-specific material
        // In production, use DPAPI or a certificate-based key. For now, derive from machine name + a salt.
        _encryptionKey = DeriveKey(config.Value.DeviceId);

        EnsureDirectory();
        LoadPendingFiles();
    }

    public void EnqueueFileEvent(FileEvent evt) => _fileEvents.Add(evt);
    public void EnqueueNetworkEvent(NetworkEvent evt) => _networkEvents.Add(evt);
    public void EnqueueAppEvent(AppUsageEvent evt) => _appEvents.Add(evt);
    public void EnqueueAlert(AlertEvent evt) => _alerts.Add(evt);

    /// <summary>
    /// Flush in-memory events to an encrypted file on disk.
    /// Returns the number of events flushed.
    /// </summary>
    public async Task<int> FlushToDiskAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            var batch = DrainBatch();
            int totalEvents = batch.FileEvents.Count + batch.NetworkEvents.Count +
                              batch.AppUsageEvents.Count + batch.Alerts.Count;

            if (totalEvents == 0) return 0;

            var json = JsonSerializer.Serialize(batch, _jsonOptions);
            var encrypted = Encrypt(Encoding.UTF8.GetBytes(json));

            var fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.enc";
            var filePath = Path.Combine(_queueDir, fileName);

            await File.WriteAllBytesAsync(filePath, encrypted);
            _pendingFiles.Enqueue(filePath);

            _logger.LogDebug("Flushed {Count} events to {File}", totalEvents, fileName);
            return totalEvents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush events to disk");
            return 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Dequeue the oldest batch from disk, decrypt and return it.
    /// Returns null if no pending batches.
    /// </summary>
    public async Task<LogBatch?> DequeueAsync()
    {
        if (!_pendingFiles.TryPeek(out var filePath))
            return null;

        try
        {
            if (!File.Exists(filePath))
            {
                _pendingFiles.TryDequeue(out _);
                return null;
            }

            var encrypted = await File.ReadAllBytesAsync(filePath);
            var decrypted = Decrypt(encrypted);
            var json = Encoding.UTF8.GetString(decrypted);
            var batch = JsonSerializer.Deserialize<LogBatch>(json, _jsonOptions);

            return batch;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dequeue batch from {File}", filePath);
            // Move corrupted file aside
            TryMoveCorrupted(filePath);
            _pendingFiles.TryDequeue(out _);
            return null;
        }
    }

    /// <summary>
    /// Mark the front batch as successfully uploaded — remove from disk.
    /// </summary>
    public void AcknowledgeDequeue()
    {
        if (_pendingFiles.TryDequeue(out var filePath))
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
                _logger.LogDebug("Acknowledged and deleted {File}", Path.GetFileName(filePath));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete acknowledged file {File}", filePath);
            }
        }
    }

    public int PendingCount => _pendingFiles.Count;

    private LogBatch DrainBatch()
    {
        var batch = new LogBatch
        {
            SentAt = DateTime.UtcNow,
            FileEvents = DrainBag(_fileEvents),
            NetworkEvents = DrainBag(_networkEvents),
            AppUsageEvents = DrainBag(_appEvents),
            Alerts = DrainBag(_alerts)
        };
        return batch;
    }

    private static List<T> DrainBag<T>(ConcurrentBag<T> bag)
    {
        var items = new List<T>();
        while (bag.TryTake(out var item))
            items.Add(item);
        return items;
    }

    private byte[] Encrypt(byte[] plaintext)
    {
        if (!_security.EncryptLocalQueue)
            return plaintext;

        using var aes = new AesGcm(_encryptionKey, AesGcm.TagByteSizes.MaxSize);
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: [nonce][tag][ciphertext]
        var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);

        return result;
    }

    private byte[] Decrypt(byte[] data)
    {
        if (!_security.EncryptLocalQueue)
            return data;

        int nonceSize = AesGcm.NonceByteSizes.MaxSize;
        int tagSize = AesGcm.TagByteSizes.MaxSize;

        var nonce = data.AsSpan(0, nonceSize);
        var tag = data.AsSpan(nonceSize, tagSize);
        var ciphertext = data.AsSpan(nonceSize + tagSize);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_encryptionKey, tagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    private static byte[] DeriveKey(string seed)
    {
        // In production: use DPAPI (ProtectedData) or X.509 cert
        var salt = Encoding.UTF8.GetBytes("LogSystem.Agent.v1");
        using var kdf = new Rfc2898DeriveBytes(seed, salt, 100_000, HashAlgorithmName.SHA256);
        return kdf.GetBytes(32); // AES-256
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_queueDir))
        {
            Directory.CreateDirectory(_queueDir);
            _logger.LogInformation("Created queue directory: {Dir}", _queueDir);
        }
    }

    private void LoadPendingFiles()
    {
        try
        {
            var files = Directory.GetFiles(_queueDir, "*.enc")
                .OrderBy(f => f)
                .ToList();

            foreach (var f in files)
                _pendingFiles.Enqueue(f);

            if (files.Count > 0)
                _logger.LogInformation("Loaded {Count} pending queue files from disk", files.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load pending queue files");
        }
    }

    private void TryMoveCorrupted(string filePath)
    {
        try
        {
            var corruptDir = Path.Combine(_queueDir, "corrupted");
            if (!Directory.Exists(corruptDir))
                Directory.CreateDirectory(corruptDir);

            var dest = Path.Combine(corruptDir, Path.GetFileName(filePath));
            File.Move(filePath, dest);
        }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
