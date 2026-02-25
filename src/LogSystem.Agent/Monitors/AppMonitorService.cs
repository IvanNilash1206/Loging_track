using System.Diagnostics;
using System.Text;
using LogSystem.Shared.Configuration;
using LogSystem.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogSystem.Agent.Monitors;

/// <summary>
/// Module 2 — Application Navigation Monitor
/// Tracks foreground window changes, process start/stop, and window titles.
/// </summary>
public sealed class AppMonitorService : IDisposable
{
    private readonly ILogger<AppMonitorService> _logger;
    private readonly AgentConfiguration _config;
    private readonly Action<AppUsageEvent> _onAppEvent;
    private Timer? _pollTimer;
    private string _lastWindowTitle = string.Empty;
    private string _lastProcessName = string.Empty;
    private int _lastProcessId;
    private DateTime _lastChangeTime = DateTime.UtcNow;

    public AppMonitorService(
        ILogger<AppMonitorService> logger,
        IOptions<AgentConfiguration> config,
        Action<AppUsageEvent> onAppEvent)
    {
        _logger = logger;
        _config = config.Value;
        _onAppEvent = onAppEvent;
    }

    public void Start()
    {
        if (!_config.AppMonitor.Enabled)
        {
            _logger.LogInformation("App monitor is disabled.");
            return;
        }

        _logger.LogInformation("Starting application navigation monitor (interval: {Ms}ms)...",
            _config.AppMonitor.PollingIntervalMs);

        _pollTimer = new Timer(
            PollForegroundWindow,
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(_config.AppMonitor.PollingIntervalMs));
    }

    private void PollForegroundWindow(object? state)
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            // Get process info
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            string processName;
            try
            {
                var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch (ArgumentException)
            {
                return; // Process exited
            }

            // Skip excluded processes
            if (_config.AppMonitor.ExcludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
                return;

            // Get window title
            var titleLength = NativeMethods.GetWindowTextLength(hwnd);
            string windowTitle = string.Empty;
            if (titleLength > 0)
            {
                var sb = new StringBuilder(titleLength + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                windowTitle = sb.ToString();
            }

            // Detect change
            if (processName != _lastProcessName || windowTitle != _lastWindowTitle || (int)pid != _lastProcessId)
            {
                var now = DateTime.UtcNow;

                // Emit event for the previous window
                if (!string.IsNullOrEmpty(_lastProcessName))
                {
                    var duration = now - _lastChangeTime;
                    if (duration.TotalSeconds >= 1) // Ignore sub-second flickers
                    {
                        var evt = new AppUsageEvent
                        {
                            DeviceId = _config.DeviceId,
                            User = Environment.UserName,
                            ApplicationName = _lastProcessName,
                            WindowTitle = _lastWindowTitle,
                            StartTime = _lastChangeTime,
                            Duration = duration,
                            ProcessId = _lastProcessId
                        };
                        _onAppEvent(evt);
                        _logger.LogDebug("App usage: {App} \"{Title}\" for {Dur:F1}s",
                            evt.ApplicationName, Truncate(evt.WindowTitle, 60), duration.TotalSeconds);
                    }
                }

                _lastProcessName = processName;
                _lastWindowTitle = windowTitle;
                _lastProcessId = (int)pid;
                _lastChangeTime = now;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling foreground window");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    public void Dispose()
    {
        _pollTimer?.Dispose();

        // Flush final window event
        if (!string.IsNullOrEmpty(_lastProcessName))
        {
            var duration = DateTime.UtcNow - _lastChangeTime;
            if (duration.TotalSeconds >= 1)
            {
                _onAppEvent(new AppUsageEvent
                {
                    DeviceId = _config.DeviceId,
                    User = Environment.UserName,
                    ApplicationName = _lastProcessName,
                    WindowTitle = _lastWindowTitle,
                    StartTime = _lastChangeTime,
                    Duration = duration,
                    ProcessId = _lastProcessId
                });
            }
        }
    }
}
