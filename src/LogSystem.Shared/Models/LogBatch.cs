namespace LogSystem.Shared.Models;

/// <summary>
/// Envelope for batching events before upload to the backend.
/// </summary>
public class LogBatch
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public List<FileEvent> FileEvents { get; set; } = [];
    public List<NetworkEvent> NetworkEvents { get; set; } = [];
    public List<AppUsageEvent> AppUsageEvents { get; set; } = [];
    public List<AlertEvent> Alerts { get; set; } = [];
    public DeviceInfo? DeviceInfo { get; set; }
}
