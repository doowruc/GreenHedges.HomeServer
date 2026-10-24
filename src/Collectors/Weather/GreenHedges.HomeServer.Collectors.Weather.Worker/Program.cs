using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Heartbeat;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using GreenHedges.HomeServer.Collectors.Weather.Storage;
using GreenHedges.HomeServer.Collectors.Weather.Summaries;
using GreenHedges.HomeServer.Collectors.Weather.Worker;
using GreenHedges.HomeServer.Collectors.Weather.Worker.Alerts;
using GreenHedges.HomeServer.Collectors.Weather.Worker.Backups;
using GreenHedges.HomeServer.Collectors.Weather.Worker.Summaries;
using Microsoft.Extensions.Options;

var builder = CollectorHost.CreateBuilder(args, "WeatherCollector");
builder.AddAlerting();
builder.AddHeartbeat();

builder.Services.AddOptions<Rtl433Options>()
    .BindConfiguration(Rtl433Options.SectionName)
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.Path) && o.SilenceTimeout > TimeSpan.Zero && o.MinRestartDelay > TimeSpan.Zero
            && o.MaxRestartDelay >= o.MinRestartDelay,
        "Rtl433:Path must be set, and the timeouts and restart delays positive (MaxRestartDelay >= MinRestartDelay).")
    .ValidateOnStart();
builder.Services.AddOptions<StationOptions>().BindConfiguration(StationOptions.SectionName);
builder.Services.AddOptions<ArchiveOptions>().BindConfiguration(ArchiveOptions.SectionName);
builder.Services.AddOptions<WeatherAlertOptions>()
    .BindConfiguration(AlertOptions.SectionName)
    .Validate(
        o => o.StationSilentAfter > TimeSpan.Zero && o.ReceiverEndsPerHour > 0 && o.BatteryLowAfter > TimeSpan.Zero
            && o.PublishStalledAfter > TimeSpan.Zero,
        "Alerts:StationSilentAfter, Alerts:ReceiverEndsPerHour, Alerts:BatteryLowAfter and Alerts:PublishStalledAfter must be positive.")
    .ValidateOnStart();

builder.Services.AddSingleton(services => services.GetRequiredService<IOptions<Rtl433Options>>().Value);
builder.Services.AddSingleton<ReceiverHealth>();
builder.Services.AddSingleton<Rtl433Runner>();
builder.Services.AddSingleton<Rtl433Supervisor>();
builder.Services.AddSingleton(services =>
    new WeatherArchive(Path.GetFullPath(services.GetRequiredService<IOptions<ArchiveOptions>>().Value.DatabasePath)));
builder.Services.AddSingleton<CollectorHealth>();
builder.Services.AddSingleton<IHeartbeatSource>(services => services.GetRequiredService<CollectorHealth>());
builder.Services.AddHostedService<CollectorService>();
builder.Services.AddHostedService<WeatherAlertMonitor>();
builder.Services.AddOptions<SummaryOptions>()
    .BindConfiguration(SummaryOptions.SectionName)
    .Validate(o => o.Interval > TimeSpan.Zero && o.BatchSize > 0, "Summaries:Interval and Summaries:BatchSize must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton(services => new SummaryStore(
    services.GetRequiredService<IConfiguration>().GetConnectionString(SummaryPublisherService.ConnectionStringName)
        ?? throw new InvalidOperationException("ConnectionStrings:Weather is not set.")));
builder.Services.AddSingleton<SummaryPublisher>();
builder.Services.AddHostedService<SummaryPublisherService>();
builder.AddBackups<WeatherArchiveBackupSource>();

var host = builder.Build();

// `--test-alert` sends one alert through the configured sender and exits, to check the Resend setup.
if (await host.TrySendTestAlertAsync(args))
{
    return 0;
}

// Create or migrate the archive before any hosted service touches it.
var archive = host.Services.GetRequiredService<WeatherArchive>();
await archive.InitialiseAsync(CancellationToken.None);
host.Services.GetRequiredService<ILogger<WeatherArchive>>().LogInformation(
    "Archive ready (schema v{Version}): {Messages:N0} messages", WeatherArchive.SchemaVersion, await archive.CountMessagesAsync(CancellationToken.None));

await host.RunAsync();
return 0;
