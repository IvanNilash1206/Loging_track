using System.Security.Cryptography;
using LogSystem.Shared.Configuration;
using LogSystem.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogSystem.Agent.Monitors;

/// <summary>
/// Module 1 — File Activity Monitor
/// Uses FileSystemWatcher to track file operations on local drives, USB, network shares, and cloud sync folders.
/// </summary>
public sealed class FileMonitorService : IDisposable
{
    private readonly ILogger<FileMonitorService> _logger;
    private readonly AgentConfiguration _config;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Action<FileEvent> _onFileEvent;
    private readonly string _currentUser;
    private readonly string _machineId;
    private Timer? _usbPollTimer;
    private readonly HashSet<string> _knownDrives = [];

    public FileMonitorService(
        ILogger<FileMonitorService> logger,
        IOptions<AgentConfiguration> config,
        Action<FileEvent> onFileEvent)
    {
        _logger = logger;
        _config = config.Value;
        _onFileEvent = onFileEvent;
        _currentUser = Environment.UserName;
        _machineId = _config.DeviceId;
    }

    public void Start()
    {
        if (!_config.FileMonitor.Enabled)
        {
            _logger.LogInformation("File monitor is disabled.");
            return;
        }

        _logger.LogInformation("Starting file monitor...");

        // Watch configured paths
        foreach (var path in _config.FileMonitor.WatchPaths)
        {
            if (Directory.Exists(path))
                AddWatcher(path, "ConfiguredPath");
        }

        // Watch sensitive directories
        foreach (var path in _config.FileMonitor.SensitiveDirectories)
        {
            if (Directory.Exists(path))
                AddWatcher(path, "SensitiveDir");
        }

        // Watch cloud sync folders
        foreach (var path in _config.FileMonitor.CloudSyncPaths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            if (Directory.Exists(expanded))
                AddWatcher(expanded, "CloudSync");
        }

        // Monitor USB drives
        if (_config.FileMonitor.MonitorUsb)
        {
            ScanForRemovableDrives();
            _usbPollTimer = new Timer(_ => ScanForRemovableDrives(), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        // Monitor network shares
        if (_config.FileMonitor.MonitorNetworkShares)
        {
            ScanForNetworkDrives();
        }

        _logger.LogInformation("File monitor started with {Count} watchers.", _watchers.Count);
    }

    private void AddWatcher(string path, string source)
    {
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.CreationTime,
                InternalBufferSize = _config.FileMonitor.InternalBufferSize,
                EnableRaisingEvents = true
            };

            watcher.Created += (s, e) => HandleEvent(e.FullPath, FileActionType.Create, source);
            watcher.Changed += (s, e) => HandleEvent(e.FullPath, FileActionType.Write, source);
            watcher.Deleted += (s, e) => HandleEvent(e.FullPath, FileActionType.Delete, source);
            watcher.Renamed += (s, e) =>
            {
                HandleEvent(e.OldFullPath, FileActionType.Rename, source, e.FullPath);
            };
            watcher.Error += (s, e) =>
            {
                _logger.LogWarning(e.GetException(), "FileSystemWatcher error on {Path}", path);
            };

            _watchers.Add(watcher);
            _logger.LogDebug("Watching {Path} (source: {Source})", path, source);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create watcher for {Path}", path);
        }
    }

    private void HandleEvent(string fullPath, FileActionType actionType, string source, string? newPath = null)
    {
        try
        {
            // Filter excluded extensions
            var ext = Path.GetExtension(fullPath)?.ToLowerInvariant();
            if (ext != null && _config.FileMonitor.ExcludedExtensions.Contains(ext))
                return;

            var fileName = Path.GetFileName(fullPath);
            long fileSize = 0;
            string? sha256 = null;

            if (actionType != FileActionType.Delete && File.Exists(fullPath))
            {
                try
                {
                    var fi = new FileInfo(fullPath);
                    fileSize = fi.Length;

                    // Compute SHA256 for sensitive directories if configured
                    if (_config.FileMonitor.ComputeSha256ForSensitive &&
                        source == "SensitiveDir" &&
                        fileSize < 100 * 1024 * 1024) // Skip files > 100MB
                    {
                        sha256 = ComputeSha256(fullPath);
                    }
                }
                catch (IOException)
                {
                    // File may be locked — that's okay
                }
            }

            // Determine the process that triggered this (best-effort)
            var processName = GetLikelyProcess();

            var fileEvent = new FileEvent
            {
                DeviceId = _machineId,
                MachineId = _machineId,
                User = _currentUser,
                FileName = fileName,
                FullPath = newPath ?? fullPath,
                FileSize = fileSize,
                Sha256 = sha256,
                ActionType = actionType,
                Timestamp = DateTime.UtcNow,
                ProcessName = processName
            };

            _onFileEvent(fileEvent);
            _logger.LogDebug("File event: {Action} {File} by {Process}", actionType, fileName, processName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file event for {Path}", fullPath);
        }
    }

    private void ScanForRemovableDrives()
    {
        try
        {
            var removable = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToHashSet();

            // Detect newly inserted drives
            foreach (var drive in removable)
            {
                if (_knownDrives.Add(drive))
                {
                    _logger.LogInformation("USB drive detected: {Drive}", drive);
                    AddWatcher(drive, "USB");
                }
            }

            // Detect removed drives
            var removed = _knownDrives.Except(removable).ToList();
            foreach (var drive in removed)
            {
                _knownDrives.Remove(drive);
                _logger.LogInformation("USB drive removed: {Drive}", drive);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning removable drives");
        }
    }

    private void ScanForNetworkDrives()
    {
        try
        {
            var networkDrives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Network && d.IsReady);

            foreach (var drive in networkDrives)
            {
                AddWatcher(drive.RootDirectory.FullName, "NetworkShare");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning network drives");
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Best-effort: get the foreground process name.
    /// This is a heuristic — FileSystemWatcher doesn't natively report which process caused the event.
    /// </summary>
    private static string GetLikelyProcess()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid > 0)
            {
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
        }
        catch { /* swallow */ }
        return "Unknown";
    }

    public void Dispose()
    {
        _usbPollTimer?.Dispose();
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }
}
