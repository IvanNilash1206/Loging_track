using Google.Cloud.Firestore;
using LogSystem.Dashboard.Data;
using Microsoft.AspNetCore.Mvc;

namespace LogSystem.Dashboard.Controllers;

/// <summary>
/// Dashboard query endpoints — provides data for the admin UI.
/// All queries go through FirestoreService → Firebase Firestore.
/// </summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly FirestoreService _firestore;

    public DashboardController(FirestoreService firestore)
    {
        _firestore = firestore;
    }

    // ─── Devices ───

    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices()
    {
        var devices = await _firestore.GetDevicesAsync();
        return Ok(devices);
    }

    // ─── Alerts ───

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] string? deviceId = null,
        [FromQuery] string? severity = null,
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 100)
    {
        var cutoff = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-hours));
        var alerts = await _firestore.GetAlertsAsync(cutoff, deviceId, severity, limit);
        return Ok(alerts);
    }

    // ─── File Events ───

    [HttpGet("file-events")]
    public async Task<IActionResult> GetFileEvents(
        [FromQuery] string? deviceId = null,
        [FromQuery] string? flag = null,
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 200)
    {
        var cutoff = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-hours));
        var events = await _firestore.GetFileEventsAsync(cutoff, deviceId, flag, limit);
        return Ok(events);
    }

    // ─── Network Events ───

    [HttpGet("network-events")]
    public async Task<IActionResult> GetNetworkEvents(
        [FromQuery] string? deviceId = null,
        [FromQuery] string? flag = null,
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 200)
    {
        var cutoff = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-hours));
        var events = await _firestore.GetNetworkEventsAsync(cutoff, deviceId, flag, limit);
        return Ok(events);
    }

    // ─── App Usage ───

    [HttpGet("app-usage")]
    public async Task<IActionResult> GetAppUsage(
        [FromQuery] string? deviceId = null,
        [FromQuery] int hours = 24,
        [FromQuery] int limit = 200)
    {
        var cutoff = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-hours));
        var events = await _firestore.GetAppUsageEventsAsync(cutoff, deviceId, limit);
        return Ok(events);
    }

    // ─── Summary / Statistics ───

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] int hours = 24)
    {
        var cutoff = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-hours));

        var totalDevices = await _firestore.CountDevicesAsync();
        var activeDevices = await _firestore.CountActiveDevicesAsync(cutoff);
        var totalAlerts = await _firestore.CountAlertsAsync(cutoff);
        var criticalAlerts = await _firestore.CountAlertsAsync(cutoff, "Critical");
        var highAlerts = await _firestore.CountAlertsAsync(cutoff, "High");
        var fileEvents = await _firestore.CountFileEventsAsync(cutoff);
        var flaggedFiles = await _firestore.CountFileEventsAsync(cutoff, flagFilter: "ProbableUpload");
        var networkEvents = await _firestore.CountNetworkEventsAsync(cutoff);

        // Top processes by network bytes sent (in-memory aggregation — fine for 25 endpoints)
        var allNetEvents = await _firestore.GetNetworkEventsForAggregationAsync(cutoff);

        var topProcesses = allNetEvents
            .GroupBy(n => n.ProcessName)
            .Select(g => new
            {
                ProcessName = g.Key,
                TotalBytesSent = g.Sum(x => x.BytesSent),
                TotalBytesReceived = g.Sum(x => x.BytesReceived),
                EventCount = g.Count()
            })
            .OrderByDescending(x => x.TotalBytesSent)
            .Take(10)
            .ToList();

        // Top apps by usage time
        var allAppEvents = await _firestore.GetAppUsageForAggregationAsync(cutoff);

        var topApps = allAppEvents
            .GroupBy(a => a.ApplicationName)
            .Select(g => new
            {
                ApplicationName = g.Key,
                TotalDurationMinutes = g.Sum(x => x.DurationSeconds) / 60.0,
                SessionCount = g.Count()
            })
            .OrderByDescending(x => x.TotalDurationMinutes)
            .Take(10)
            .ToList();

        return Ok(new
        {
            period = $"Last {hours} hours",
            totalDevices,
            activeDevices,
            totalAlerts,
            criticalAlerts,
            highAlerts,
            fileEvents,
            flaggedFiles,
            networkEvents,
            topProcesses,
            topApps
        });
    }

    // ─── Top Talkers ───

    [HttpGet("top-talkers")]
    public async Task<IActionResult> GetTopTalkers([FromQuery] int hours = 24, [FromQuery] int limit = 10)
    {
        var cutoff = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-hours));
        var allNetEvents = await _firestore.GetNetworkEventsForAggregationAsync(cutoff);

        var topTalkers = allNetEvents
            .GroupBy(n => new { n.DeviceId, n.User })
            .Select(g => new
            {
                g.Key.DeviceId,
                g.Key.User,
                TotalBytesSent = g.Sum(x => x.BytesSent),
                TotalBytesReceived = g.Sum(x => x.BytesReceived),
                UniqueDestinations = g.Select(x => x.DestinationIp).Distinct().Count()
            })
            .OrderByDescending(x => x.TotalBytesSent)
            .Take(limit)
            .ToList();

        return Ok(topTalkers);
    }
}
