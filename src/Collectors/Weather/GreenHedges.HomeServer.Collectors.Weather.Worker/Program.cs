using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Heartbeat;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Collectors.Weather.History;
using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using GreenHedges.HomeServer.Collectors.Weather.Storage;
using GreenHedges.HomeServer.Collectors.Weather.Summaries;
using GreenHedges.HomeServer.Collectors.Weather.Worker;
using GreenHedges.HomeServer.Collectors.Weather.Worker.Alerts;
using GreenHedges.HomeServer.Collectors.Weather.Worker.Backups;
using GreenHedges.HomeServer.Collectors.Weather.Worker.History;
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
            && o.PublishStalledAfter > TimeSpan.Zero && o.WundergroundFailingAfter > TimeSpan.Zero,
        "Alerts:StationSilentAfter, ReceiverEndsPerHour, BatteryLowAfter, PublishStalledAfter and WundergroundFailingAfter must be positive.")
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
builder.Services.AddSingleton<HistoryPublisher>();
builder.Services.AddOptions<WundergroundOptions>()
    .BindConfiguration(WundergroundOptions.SectionName)
    .Validate(
        o => o.MaxCallsPerDay > 0 && o.CallInterval >= TimeSpan.Zero && o.RunAt >= TimeSpan.Zero && o.RunAt < TimeSpan.FromDays(1)
            && (o.TodayInterval == TimeSpan.Zero || o.TodayInterval >= TimeSpan.FromMinutes(5))
            && TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _),
        "Wunderground: MaxCallsPerDay must be positive, RunAt a time of day, TodayInterval 0 or at least 5 minutes, and TimeZone a known time zone.")
    .ValidateOnStart();
// A long-lived HttpClient; PooledConnectionLifetime picks up DNS changes without IHttpClientFactory.
builder.Services.AddSingleton(services => new WundergroundClient(
    new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TimeSpan.FromSeconds(60) },
    services.GetRequiredService<IOptions<WundergroundOptions>>()));
builder.Services.AddSingleton<WundergroundImporter>();
builder.Services.AddSingleton<WeathercloudImporter>();
builder.Services.AddHostedService<WundergroundService>();
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

// `--fetch-wunderground [yyyy-MM-dd]` fetches one local day (default: yesterday) from Weather Underground and prints
// what came back, without storing it: a check of the station id and API key.
var fetchIndex = Array.IndexOf(args, "--fetch-wunderground");
if (fetchIndex >= 0)
{
    var wunderground = host.Services.GetRequiredService<IOptions<WundergroundOptions>>().Value;
    if (!wunderground.Enabled)
    {
        Console.Error.WriteLine("Wunderground:StationId and Wunderground:ApiKey must be set (env file: Wunderground__StationId, Wunderground__ApiKey).");
        return 2;
    }

    var zone = TimeZoneInfo.FindSystemTimeZoneById(wunderground.TimeZone);
    var day = fetchIndex + 1 < args.Length
        ? DateOnly.ParseExact(args[fetchIndex + 1], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime).AddDays(-1);
    var response = await host.Services.GetRequiredService<WundergroundClient>().FetchDayAsync(day, CancellationToken.None);
    var observations = response.Observations;
    Console.WriteLine(observations.Count == 0
        ? $"Weather Underground, {day:yyyy-MM-dd}: no observations."
        : $"""
          Weather Underground, {day:yyyy-MM-dd}: {observations.Count} observations, {observations[0].ObsTimeUtc:u} to {observations[^1].ObsTimeUtc:u}
            Temperature {observations.Min(o => o.TempLowC)} to {observations.Max(o => o.TempHighC)} °C, rain {observations[^1].PrecipTotalMm} mm,
            pressure {observations.Min(o => o.PressureMinHpa)} to {observations.Max(o => o.PressureMaxHpa)} hPa, strongest gust {observations.Max(o => o.GustHighKmh)} km/h
          """);
    return 0;
}

// `--import-weathercloud <file.csv|file.zip|folder>...` imports Weathercloud exports into the archive and exits. Safe to
// run while the service is running (which then publishes them), and to re-run: each day replaces its earlier import.
var weathercloudIndex = Array.IndexOf(args, "--import-weathercloud");
if (weathercloudIndex >= 0)
{
    var paths = args.Skip(weathercloudIndex + 1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
    if (paths.Count == 0 || paths.FirstOrDefault(p => !File.Exists(p) && !Directory.Exists(p)) is { } missing)
    {
        Console.Error.WriteLine("Usage: GreenHedges.HomeServer.Collectors.Weather.Worker --import-weathercloud <file.csv|file.zip|folder>...");
        Console.Error.WriteLine(paths.Count == 0 ? "Nothing given." : $"Not found: {paths.First(p => !File.Exists(p) && !Directory.Exists(p))}");
        return 2;
    }

    var results = await host.Services.GetRequiredService<WeathercloudImporter>().ImportAsync(paths, CancellationToken.None);
    foreach (var f in results)
    {
        Console.WriteLine(
            $"{f.File}: {f.RowsImported:N0} rows in {f.Days} days" +
            (f.FirstUtc is { } first ? $" ({first:u} to {f.LastUtc:u})" : "") +
            $"; skipped {f.SkippedEmpty:N0} empty, {f.SkippedInvalidTime} invalid time, {f.SkippedUnparseable} unparseable");
    }

    Console.WriteLine($"""
        Weathercloud import: {results.Count} files, {results.Sum(f => f.RowsImported):N0} rows in {results.Sum(f => f.Days):N0} days
        ({results.Sum(f => f.SkippedEmpty):N0} empty rows skipped). The running service publishes them to SQL Server within a minute or two.
        """);
    return 0;
}

await host.RunAsync();
return 0;
