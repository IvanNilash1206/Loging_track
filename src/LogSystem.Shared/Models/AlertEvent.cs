namespace LogSystem.Shared.Models;

public class AlertEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DeviceId { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;
    public string AlertType { get; set; } = string.Empty; // LargeTransfer | ContinuousTransfer | ProbableUpload
    public string Description { get; set; } = string.Empty;
    public string? RelatedFileName { get; set; }
    public string? RelatedProcessName { get; set; }
    public long? BytesInvolved { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum AlertSeverity
{
    Low,
    Medium,
    High,
    Critical
}
