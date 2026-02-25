using Google.Cloud.Firestore;

namespace LogSystem.Dashboard.Data;

/// <summary>
/// Firestore data access service.
/// Provides typed CRUD operations for all LogSystem collections.
/// All queries use single-field filters only (no composite indexes required).
/// Additional filtering is done in-memory — appropriate for 25 endpoints.
/// </summary>
public class FirestoreService
{
    private readonly FirestoreDb _db;
    private readonly ILogger<FirestoreService> _logger;

    // Collection names
    private const string DevicesCollection = "devices";
    private const string FileEventsCollection = "file_events";
    private const string NetworkEventsCollection = "network_events";
    private const string AppUsageCollection = "app_usage_events";
    private const string AlertsCollection = "alert_events";

    public FirestoreService(FirestoreDb db, ILogger<FirestoreService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ──────── Devices ────────

    public async Task UpsertDeviceAsync(DeviceEntity device)
    {
        var docRef = _db.Collection(DevicesCollection).Document(device.DeviceId);
        await docRef.SetAsync(device, SetOptions.MergeAll);
    }

    public async Task<List<DeviceEntity>> GetDevicesAsync()
    {
        var snapshot = await _db.Collection(DevicesCollection).GetSnapshotAsync();

        return snapshot.Documents
            .Select(d => d.ConvertTo<DeviceEntity>())
            .OrderByDescending(d => d.LastSeen.ToDateTime())
            .ToList();
    }

    public async Task<int> CountDevicesAsync()
    {
        var snapshot = await _db.Collection(DevicesCollection).GetSnapshotAsync();
        return snapshot.Count;
    }

    public async Task<int> CountActiveDevicesAsync(Timestamp cutoff)
    {
        var snapshot = await _db.Collection(DevicesCollection).GetSnapshotAsync();
        return snapshot.Documents
            .Select(d => d.ConvertTo<DeviceEntity>())
            .Count(d => d.LastSeen >= cutoff);
    }

    // ──────── File Events ────────

    public async Task AddFileEventAsync(FileEventEntity entity)
    {
        var docRef = _db.Collection(FileEventsCollection).Document(entity.Id);
        await docRef.SetAsync(entity);
    }

    public async Task AddFileEventsBatchAsync(IEnumerable<FileEventEntity> entities)
    {
        var batch = _db.StartBatch();
        foreach (var entity in entities)
        {
            var docRef = _db.Collection(FileEventsCollection).Document(entity.Id);
            batch.Set(docRef, entity);
        }
        await batch.CommitAsync();
    }

    public async Task<List<FileEventEntity>> GetFileEventsAsync(
        Timestamp cutoff, string? deviceId = null, string? flag = null, int limit = 200)
    {
        var snapshot = await _db.Collection(FileEventsCollection).GetSnapshotAsync();

        var results = snapshot.Documents
            .Select(d => d.ConvertTo<FileEventEntity>())
            .Where(e => e.Timestamp >= cutoff);

        if (!string.IsNullOrEmpty(deviceId))
            results = results.Where(e => e.DeviceId == deviceId);
        if (!string.IsNullOrEmpty(flag))
            results = results.Where(e => e.Flag == flag);

        return results
            .OrderByDescending(e => e.Timestamp.ToDateTime())
            .Take(limit)
            .ToList();
    }

    public async Task<int> CountFileEventsAsync(Timestamp cutoff, string? flagFilter = null)
    {
        var snapshot = await _db.Collection(FileEventsCollection).GetSnapshotAsync();

        var results = snapshot.Documents
            .Select(d => d.ConvertTo<FileEventEntity>())
            .Where(e => e.Timestamp >= cutoff);

        if (!string.IsNullOrEmpty(flagFilter))
            results = results.Where(e => e.Flag == flagFilter);

        return results.Count();
    }

    // ──────── Network Events ────────

    public async Task AddNetworkEventAsync(NetworkEventEntity entity)
    {
        var docRef = _db.Collection(NetworkEventsCollection).Document(entity.Id);
        await docRef.SetAsync(entity);
    }

    public async Task AddNetworkEventsBatchAsync(IEnumerable<NetworkEventEntity> entities)
    {
        var batch = _db.StartBatch();
        foreach (var entity in entities)
        {
            var docRef = _db.Collection(NetworkEventsCollection).Document(entity.Id);
            batch.Set(docRef, entity);
        }
        await batch.CommitAsync();
    }

    public async Task<List<NetworkEventEntity>> GetNetworkEventsAsync(
        Timestamp cutoff, string? deviceId = null, string? flag = null, int limit = 200)
    {
        var snapshot = await _db.Collection(NetworkEventsCollection).GetSnapshotAsync();

        var results = snapshot.Documents
            .Select(d => d.ConvertTo<NetworkEventEntity>())
            .Where(e => e.Timestamp >= cutoff);

        if (!string.IsNullOrEmpty(deviceId))
            results = results.Where(e => e.DeviceId == deviceId);
        if (!string.IsNullOrEmpty(flag))
            results = results.Where(e => e.Flag == flag);

        return results
            .OrderByDescending(e => e.Timestamp.ToDateTime())
            .Take(limit)
            .ToList();
    }

    public async Task<int> CountNetworkEventsAsync(Timestamp cutoff)
    {
        var snapshot = await _db.Collection(NetworkEventsCollection).GetSnapshotAsync();
        return snapshot.Documents
            .Select(d => d.ConvertTo<NetworkEventEntity>())
            .Count(e => e.Timestamp >= cutoff);
    }

    /// <summary>
    /// Get network events for aggregation (top processes, top talkers).
    /// Returns all events within cutoff for in-memory grouping.
    /// </summary>
    public async Task<List<NetworkEventEntity>> GetNetworkEventsForAggregationAsync(Timestamp cutoff)
    {
        var snapshot = await _db.Collection(NetworkEventsCollection).GetSnapshotAsync();

        return snapshot.Documents
            .Select(d => d.ConvertTo<NetworkEventEntity>())
            .Where(e => e.Timestamp >= cutoff)
            .ToList();
    }

    // ──────── App Usage Events ────────

    public async Task AddAppUsageEventAsync(AppUsageEventEntity entity)
    {
        var docRef = _db.Collection(AppUsageCollection).Document(entity.Id);
        await docRef.SetAsync(entity);
    }

    public async Task AddAppUsageEventsBatchAsync(IEnumerable<AppUsageEventEntity> entities)
    {
        var batch = _db.StartBatch();
        foreach (var entity in entities)
        {
            var docRef = _db.Collection(AppUsageCollection).Document(entity.Id);
            batch.Set(docRef, entity);
        }
        await batch.CommitAsync();
    }

    public async Task<List<AppUsageEventEntity>> GetAppUsageEventsAsync(
        Timestamp cutoff, string? deviceId = null, int limit = 200)
    {
        var snapshot = await _db.Collection(AppUsageCollection).GetSnapshotAsync();

        var results = snapshot.Documents
            .Select(d => d.ConvertTo<AppUsageEventEntity>())
            .Where(e => e.StartTime >= cutoff);

        if (!string.IsNullOrEmpty(deviceId))
            results = results.Where(e => e.DeviceId == deviceId);

        return results
            .OrderByDescending(e => e.StartTime.ToDateTime())
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Get app usage events for aggregation (top apps by time).
    /// </summary>
    public async Task<List<AppUsageEventEntity>> GetAppUsageForAggregationAsync(Timestamp cutoff)
    {
        var snapshot = await _db.Collection(AppUsageCollection).GetSnapshotAsync();

        return snapshot.Documents
            .Select(d => d.ConvertTo<AppUsageEventEntity>())
            .Where(e => e.StartTime >= cutoff)
            .ToList();
    }

    // ──────── Alert Events ────────

    public async Task AddAlertEventAsync(AlertEventEntity entity)
    {
        var docRef = _db.Collection(AlertsCollection).Document(entity.Id);
        await docRef.SetAsync(entity);
    }

    public async Task AddAlertEventsBatchAsync(IEnumerable<AlertEventEntity> entities)
    {
        var batch = _db.StartBatch();
        foreach (var entity in entities)
        {
            var docRef = _db.Collection(AlertsCollection).Document(entity.Id);
            batch.Set(docRef, entity);
        }
        await batch.CommitAsync();
    }

    public async Task<List<AlertEventEntity>> GetAlertsAsync(
        Timestamp cutoff, string? deviceId = null, string? severity = null, int limit = 100)
    {
        var snapshot = await _db.Collection(AlertsCollection).GetSnapshotAsync();

        var results = snapshot.Documents
            .Select(d => d.ConvertTo<AlertEventEntity>())
            .Where(e => e.Timestamp >= cutoff);

        if (!string.IsNullOrEmpty(deviceId))
            results = results.Where(e => e.DeviceId == deviceId);
        if (!string.IsNullOrEmpty(severity))
            results = results.Where(e => e.Severity == severity);

        return results
            .OrderByDescending(e => e.Timestamp.ToDateTime())
            .Take(limit)
            .ToList();
    }

    public async Task<int> CountAlertsAsync(Timestamp cutoff, string? severity = null)
    {
        var snapshot = await _db.Collection(AlertsCollection).GetSnapshotAsync();

        var results = snapshot.Documents
            .Select(d => d.ConvertTo<AlertEventEntity>())
            .Where(e => e.Timestamp >= cutoff);

        if (!string.IsNullOrEmpty(severity))
            results = results.Where(e => e.Severity == severity);

        return results.Count();
    }
}
