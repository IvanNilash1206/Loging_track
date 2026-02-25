namespace LogSystem.Shared.Models;

public class NetworkEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DeviceId { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public string DestinationIp { get; set; } = string.Empty;
    public int DestinationPort { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Flag { get; set; } = "Normal"; // Normal | LargeTransfer | ContinuousTransfer
}
