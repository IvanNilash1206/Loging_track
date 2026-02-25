using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LogSystem.Shared.Configuration;
using LogSystem.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogSystem.Agent.Services;

/// <summary>
/// Secure Log Uploader — sends encrypted batches to the backend API over TLS.
/// Implements retry with exponential backoff.
/// </summary>
public sealed class LogUploaderService : IDisposable
{
    private readonly ILogger<LogUploaderService> _logger;
    private readonly AgentConfiguration _config;
    private readonly LocalEventQueue _queue;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private Timer? _uploadTimer;
    private bool _uploading;
    private int _consecutiveFailures;
    private const int MaxRetries = 3;
    private const int MaxBackoffSeconds = 300; // 5 min cap

    public LogUploaderService(
        ILogger<LogUploaderService> logger,
        IOptions<AgentConfiguration> config,
        LocalEventQueue queue,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _config = config.Value;
        _queue = queue;
        _httpClient = httpClientFactory.CreateClient("LogUploader");

        // Configure client
        _httpClient.BaseAddress = new Uri(_config.ApiEndpoint);
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _config.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("X-Device-Id", _config.DeviceId);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Start()
    {
        _logger.LogInformation("Starting log uploader (interval: {Sec}s, endpoint: {Url})",
            _config.UploadIntervalSeconds, _config.ApiEndpoint);

        _uploadTimer = new Timer(
            async _ => await UploadPendingBatchesAsync(),
            null,
            TimeSpan.FromSeconds(10), // Initial delay
            TimeSpan.FromSeconds(_config.UploadIntervalSeconds));
    }

    private async Task UploadPendingBatchesAsync()
    {
        if (_uploading) return;
        _uploading = true;

        try
        {
            // First flush in-memory events to disk
            var flushed = await _queue.FlushToDiskAsync();
            if (flushed > 0)
                _logger.LogDebug("Flushed {Count} events to queue", flushed);

            // Upload pending batches
            int uploaded = 0;
            while (_queue.PendingCount > 0)
            {
                var batch = await _queue.DequeueAsync();
                if (batch == null) break;

                // Set device info
                batch.DeviceId = _config.DeviceId;
                batch.DeviceInfo = new DeviceInfo
                {
                    DeviceId = _config.DeviceId,
                    Hostname = Environment.MachineName,
                    User = Environment.UserName,
                    LastSeen = DateTime.UtcNow,
                    OsVersion = Environment.OSVersion.ToString(),
                    AgentVersion = GetAgentVersion()
                };

                var success = await SendBatchWithRetryAsync(batch);
                if (success)
                {
                    _queue.AcknowledgeDequeue();
                    uploaded++;
                    _consecutiveFailures = 0;
                }
                else
                {
                    _consecutiveFailures++;
                    _logger.LogWarning("Upload failed. Consecutive failures: {Count}. Will retry next cycle.",
                        _consecutiveFailures);

                    // Apply backoff — skip remaining batches this cycle
                    break;
                }
            }

            if (uploaded > 0)
                _logger.LogInformation("Uploaded {Count} batches. Remaining: {Remaining}",
                    uploaded, _queue.PendingCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in upload cycle");
        }
        finally
        {
            _uploading = false;
        }
    }

    private async Task<bool> SendBatchWithRetryAsync(LogBatch batch)
    {
        var json = JsonSerializer.Serialize(batch, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var response = await _httpClient.PostAsync("/api/logs/ingest", content);

                if (response.IsSuccessStatusCode)
                    return true;

                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Upload attempt {Attempt} failed: {Status} — {Body}",
                    attempt + 1, response.StatusCode, body);

                // Don't retry client errors (4xx) except 429
                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500
                    && response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                {
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Upload attempt {Attempt} network error", attempt + 1);
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Upload attempt {Attempt} timed out", attempt + 1);
            }

            // Exponential backoff
            var delay = Math.Min(Math.Pow(2, attempt + _consecutiveFailures) * 1000, MaxBackoffSeconds * 1000);
            await Task.Delay((int)delay);
        }

        return false;
    }

    /// <summary>
    /// Force an immediate upload (e.g., for critical alerts).
    /// </summary>
    public async Task ForceUploadAsync()
    {
        _logger.LogInformation("Force upload triggered");
        await UploadPendingBatchesAsync();
    }

    private static string GetAgentVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "1.0.0";
    }

    public void Dispose()
    {
        _uploadTimer?.Dispose();
        _httpClient.Dispose();
    }
}
