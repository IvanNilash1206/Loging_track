using Google.Cloud.Firestore;
using LogSystem.Dashboard.Data;
using LogSystem.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LogSystem.Dashboard.Controllers;

/// <summary>
/// Receives log batches from agents.
/// Validates API key and persists events to Firebase Firestore.
/// </summary>
[ApiController]
[Route("api/logs")]
public class LogIngestionController : ControllerBase
{
    private readonly FirestoreService _firestore;
    private readonly ILogger<LogIngestionController> _logger;
    private readonly IConfiguration _configuration;

    public LogIngestionController(
        FirestoreService firestore,
        ILogger<LogIngestionController> logger,
        IConfiguration configuration)
    {
        _firestore = firestore;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] LogBatch batch)
    {
        // Validate API key
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        var expectedKey = _configuration["Dashboard:ApiKey"];
        if (string.IsNullOrEmpty(apiKey) || apiKey != expectedKey)
        {
            _logger.LogWarning("Unauthorized ingest attempt from {Ip}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid API key" });
        }

        if (batch == null)
            return BadRequest(new { error = "Empty batch" });

        try
        {
            // Upsert device info
            if (batch.DeviceInfo != null)
            {
                await _firestore.UpsertDeviceAsync(new DeviceEntity
                {
                    DeviceId = batch.DeviceInfo.DeviceId,
                    Hostname = batch.DeviceInfo.Hostname,
                    User = batch.DeviceInfo.User,
                    LastSeen = Timestamp.FromDateTime(batch.DeviceInfo.LastSeen.ToUniversalTime()),
                    OsVersion = batch.DeviceInfo.OsVersion,
                    AgentVersion = batch.DeviceInfo.AgentVersion
                });
            }

            // Persist file events (batch write — max 500 per Firestore batch)
            if (batch.FileEvents.Count > 0)
            {
                var entities = batch.FileEvents.Select(fe => new FileEventEntity
                {
                    Id = fe.Id,
                    DeviceId = fe.DeviceId,
                    User = fe.User,
                    FileName = fe.FileName,
                    FullPath = fe.FullPath,
                    FileSize = fe.FileSize,
                    Sha256 = fe.Sha256,
                    ActionType = fe.ActionType.ToString(),
                    Timestamp = Timestamp.FromDateTime(fe.Timestamp.ToUniversalTime()),
                    ProcessName = fe.ProcessName,
                    Flag = fe.Flag
                });

                // Firestore batch writes are limited to 500 — chunk if needed
                foreach (var chunk in Chunk(entities, 450))
                    await _firestore.AddFileEventsBatchAsync(chunk);
            }

            // Persist network events
            if (batch.NetworkEvents.Count > 0)
            {
                var entities = batch.NetworkEvents.Select(ne => new NetworkEventEntity
                {
                    Id = ne.Id,
                    DeviceId = ne.DeviceId,
                    User = ne.User,
                    ProcessName = ne.ProcessName,
                    ProcessId = ne.ProcessId,
                    BytesSent = ne.BytesSent,
                    BytesReceived = ne.BytesReceived,
                    DestinationIp = ne.DestinationIp,
                    DestinationPort = ne.DestinationPort,
                    DurationSeconds = ne.Duration.TotalSeconds,
                    Timestamp = Timestamp.FromDateTime(ne.Timestamp.ToUniversalTime()),
                    Flag = ne.Flag
                });

                foreach (var chunk in Chunk(entities, 450))
                    await _firestore.AddNetworkEventsBatchAsync(chunk);
            }

            // Persist app usage events
            if (batch.AppUsageEvents.Count > 0)
            {
                var entities = batch.AppUsageEvents.Select(ae => new AppUsageEventEntity
                {
                    Id = ae.Id,
                    DeviceId = ae.DeviceId,
                    User = ae.User,
                    ApplicationName = ae.ApplicationName,
                    WindowTitle = ae.WindowTitle,
                    StartTime = Timestamp.FromDateTime(ae.StartTime.ToUniversalTime()),
                    DurationSeconds = ae.Duration.TotalSeconds,
                    ProcessId = ae.ProcessId
                });

                foreach (var chunk in Chunk(entities, 450))
                    await _firestore.AddAppUsageEventsBatchAsync(chunk);
            }

            // Persist alerts
            if (batch.Alerts.Count > 0)
            {
                var entities = batch.Alerts.Select(alert => new AlertEventEntity
                {
                    Id = alert.Id,
                    DeviceId = alert.DeviceId,
                    User = alert.User,
                    Severity = alert.Severity.ToString(),
                    AlertType = alert.AlertType,
                    Description = alert.Description,
                    RelatedFileName = alert.RelatedFileName,
                    RelatedProcessName = alert.RelatedProcessName,
                    BytesInvolved = alert.BytesInvolved,
                    Timestamp = Timestamp.FromDateTime(alert.Timestamp.ToUniversalTime())
                });

                foreach (var chunk in Chunk(entities, 450))
                    await _firestore.AddAlertEventsBatchAsync(chunk);
            }

            var total = batch.FileEvents.Count + batch.NetworkEvents.Count +
                        batch.AppUsageEvents.Count + batch.Alerts.Count;

            _logger.LogInformation("Ingested {Count} events from device {DeviceId}",
                total, batch.DeviceId);

            return Ok(new { received = total });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ingesting batch from {DeviceId}", batch.DeviceId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Split a sequence into chunks of a given size.
    /// </summary>
    private static IEnumerable<IEnumerable<T>> Chunk<T>(IEnumerable<T> source, int size)
    {
        var list = source.ToList();
        for (int i = 0; i < list.Count; i += size)
            yield return list.Skip(i).Take(size);
    }
}
