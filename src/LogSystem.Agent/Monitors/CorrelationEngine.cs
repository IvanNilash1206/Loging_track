using System.Collections.Concurrent;
using LogSystem.Shared.Configuration;
using LogSystem.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogSystem.Agent.Monitors;

/// <summary>
/// Module 4 — Correlation Engine
/// Applies rules to detect large transfers, continuous exfiltration, and probable file uploads.
/// </summary>
public sealed class CorrelationEngine : IDisposable
{
    private readonly ILogger<CorrelationEngine> _logger;
    private readonly CorrelationConfig _config;
    private readonly string _deviceId;
    private readonly Action<AlertEvent> _onAlert;
    private readonly Action<NetworkEvent> _onFlaggedNetwork;
    private readonly Action<FileEvent> _onFlaggedFile;

    // Rolling window for network events per process
    private readonly ConcurrentDictionary<string, List<TimestampedBytes>> _processOutbound = new();

    // Recent file read events for correlation with network sends
    private readonly ConcurrentQueue<FileReadRecord> _recentFileReads = new();

    private Timer? _cleanupTimer;

    public CorrelationEngine(
        ILogger<CorrelationEngine> logger,
        IOptions<AgentConfiguration> config,
        Action<AlertEvent> onAlert,
        Action<NetworkEvent> onFlaggedNetwork,
        Action<FileEvent> onFlaggedFile)
    {
        _logger = logger;
        _config = config.Value.Correlation;
        _deviceId = config.Value.DeviceId;
        _onAlert = onAlert;
        _onFlaggedNetwork = onFlaggedNetwork;
        _onFlaggedFile = onFlaggedFile;

        // Periodic cleanup of old data
        _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Process an incoming network event through correlation rules.
    /// </summary>
    public void ProcessNetworkEvent(NetworkEvent evt)
    {
        if (!_config.Enabled) return;

        // --- Rule 1: Large Transfer Detection ---
        if (evt.BytesSent >= _config.LargeTransferThresholdBytes)
        {
            evt.Flag = "LargeTransfer";
            _onFlaggedNetwork(evt);

            _onAlert(new AlertEvent
            {
                DeviceId = _deviceId,
                User = evt.User,
                Severity = AlertSeverity.High,
                AlertType = "LargeTransfer",
                Description = $"Process '{evt.ProcessName}' sent {FormatBytes(evt.BytesSent)} to {evt.DestinationIp}",
                RelatedProcessName = evt.ProcessName,
                BytesInvolved = evt.BytesSent,
                Timestamp = evt.Timestamp
            });

            _logger.LogWarning("ALERT: Large transfer — {Process} sent {Bytes} to {Ip}",
                evt.ProcessName, FormatBytes(evt.BytesSent), evt.DestinationIp);
        }

        // --- Rule 2: Continuous Small Transfers (Rolling Window) ---
        var key = evt.ProcessName.ToLowerInvariant();
        var entry = new TimestampedBytes(evt.Timestamp, evt.BytesSent);

        _processOutbound.AddOrUpdate(
            key,
            _ => [entry],
            (_, list) => { list.Add(entry); return list; });

        CheckContinuousTransfer(key, evt);

        // --- Rule 3: Probable File Upload ---
        CheckProbableUpload(evt);
    }

    /// <summary>
    /// Register a file read event for later correlation with network uploads.
    /// </summary>
    public void RegisterFileRead(FileEvent evt)
    {
        if (!_config.Enabled) return;

        if (evt.ActionType == FileActionType.Read || evt.ActionType == FileActionType.Write)
        {
            _recentFileReads.Enqueue(new FileReadRecord
            {
                FileName = evt.FileName,
                FullPath = evt.FullPath,
                FileSize = evt.FileSize,
                ProcessName = evt.ProcessName,
                Timestamp = evt.Timestamp
            });
        }
    }

    /// <summary>
    /// Rule 2 — Continuous Small Transfers
    /// If total outbound over a rolling window exceeds threshold from multiple connections, flag it.
    /// </summary>
    private void CheckContinuousTransfer(string processKey, NetworkEvent triggerEvt)
    {
        if (!_processOutbound.TryGetValue(processKey, out var entries))
            return;

        var windowStart = DateTime.UtcNow.AddMinutes(-_config.ContinuousTransferWindowMinutes);
        var windowEntries = entries.Where(e => e.Timestamp >= windowStart).ToList();
        var totalBytes = windowEntries.Sum(e => e.Bytes);

        if (totalBytes >= _config.ContinuousTransferThresholdBytes && windowEntries.Count >= 3)
        {
            triggerEvt.Flag = "ContinuousTransfer";
            _onFlaggedNetwork(triggerEvt);

            _onAlert(new AlertEvent
            {
                DeviceId = _deviceId,
                User = triggerEvt.User,
                Severity = AlertSeverity.High,
                AlertType = "ContinuousTransfer",
                Description = $"Process '{triggerEvt.ProcessName}' sent {FormatBytes(totalBytes)} over {_config.ContinuousTransferWindowMinutes} min via {windowEntries.Count} transfers",
                RelatedProcessName = triggerEvt.ProcessName,
                BytesInvolved = totalBytes,
                Timestamp = triggerEvt.Timestamp
            });

            _logger.LogWarning("ALERT: Continuous transfer — {Process} sent {Bytes} in rolling window",
                triggerEvt.ProcessName, FormatBytes(totalBytes));

            // Reset window after alert to avoid repeated alerts
            entries.Clear();
        }
    }

    /// <summary>
    /// Rule 3 — Probable File Upload
    /// If a file was read by a process, and that same process sends > threshold bytes within time window, flag it.
    /// </summary>
    private void CheckProbableUpload(NetworkEvent netEvt)
    {
        var cutoff = netEvt.Timestamp.AddSeconds(-_config.ProbableUploadWindowSeconds);

        // Find file reads from the same process within the time window
        var candidates = _recentFileReads
            .Where(fr =>
                fr.ProcessName.Equals(netEvt.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                fr.Timestamp >= cutoff &&
                fr.Timestamp <= netEvt.Timestamp)
            .ToList();

        if (candidates.Count == 0) return;

        if (netEvt.BytesSent >= _config.ProbableUploadThresholdBytes)
        {
            foreach (var candidate in candidates)
            {
                var flaggedFile = new FileEvent
                {
                    DeviceId = _deviceId,
                    User = netEvt.User,
                    FileName = candidate.FileName,
                    FullPath = candidate.FullPath,
                    FileSize = candidate.FileSize,
                    ProcessName = candidate.ProcessName,
                    ActionType = FileActionType.Read,
                    Timestamp = candidate.Timestamp,
                    Flag = "ProbableUpload"
                };
                _onFlaggedFile(flaggedFile);

                _onAlert(new AlertEvent
                {
                    DeviceId = _deviceId,
                    User = netEvt.User,
                    Severity = AlertSeverity.Critical,
                    AlertType = "ProbableUpload",
                    Description = $"File '{candidate.FileName}' likely uploaded via {netEvt.ProcessName} to {netEvt.DestinationIp} ({FormatBytes(netEvt.BytesSent)} sent within {_config.ProbableUploadWindowSeconds}s of file read)",
                    RelatedFileName = candidate.FileName,
                    RelatedProcessName = netEvt.ProcessName,
                    BytesInvolved = netEvt.BytesSent,
                    Timestamp = netEvt.Timestamp
                });

                _logger.LogWarning("ALERT: Probable upload — '{File}' via {Process} to {Ip}",
                    candidate.FileName, netEvt.ProcessName, netEvt.DestinationIp);
            }
        }
    }

    private void Cleanup(object? state)
    {
        // Clean old file read records
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        while (_recentFileReads.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
        {
            _recentFileReads.TryDequeue(out _);
        }

        // Clean old network window entries
        var windowCutoff = DateTime.UtcNow.AddMinutes(-_config.ContinuousTransferWindowMinutes * 2);
        foreach (var kvp in _processOutbound)
        {
            kvp.Value.RemoveAll(e => e.Timestamp < windowCutoff);
        }
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F2} MB",
            >= 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes} B"
        };
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }

    #region Internal Types

    private record TimestampedBytes(DateTime Timestamp, long Bytes);

    private class FileReadRecord
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    #endregion
}
