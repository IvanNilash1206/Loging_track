using LogSystem.Agent;
using LogSystem.Agent.Services;
using LogSystem.Shared.Configuration;

var builder = Host.CreateApplicationBuilder(args);

// Bind configuration
builder.Services.Configure<AgentConfiguration>(
    builder.Configuration.GetSection("AgentConfiguration"));

// Generate DeviceId if not set
builder.Services.PostConfigure<AgentConfiguration>(config =>
{
    if (string.IsNullOrEmpty(config.DeviceId))
    {
        config.DeviceId = $"{Environment.MachineName}-{Environment.UserName}".ToUpperInvariant();
    }

    // Auto-populate default cloud sync paths if not configured
    if (config.FileMonitor.CloudSyncPaths.Count == 0)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaults = new[]
        {
            Path.Combine(userProfile, "OneDrive"),
            Path.Combine(userProfile, "Google Drive"),
            Path.Combine(userProfile, "Dropbox"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "DriveFS")
        };
        foreach (var p in defaults)
        {
            if (Directory.Exists(p))
                config.FileMonitor.CloudSyncPaths.Add(p);
        }
    }
});

// Register services
builder.Services.AddSingleton<LocalEventQueue>();
builder.Services.AddHttpClient("LogUploader")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // Enforce TLS 1.2+
        SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
    });
builder.Services.AddSingleton<LogUploaderService>();
builder.Services.AddHostedService<Worker>();

// Windows Service support
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "LogSystem Agent";
});

var host = builder.Build();
host.Run();

