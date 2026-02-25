namespace LogSystem.Shared.Models;

public class AppUsageEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DeviceId { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
    public int ProcessId { get; set; }
}
