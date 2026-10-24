using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using GreenHedges.HomeServer.Collectors.Weather.Storage;
using GreenHedges.HomeServer.Collectors.Weather.Worker.Alerts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Collectors.Weather.Worker.Tests;

public sealed class WeatherAlertMonitorTests : IDisposable
{
    private const string StationId = "12345678";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingSender _sender = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"weather-alerts-{Guid.NewGuid():N}");
    private readonly WeatherArchive _archive;
    private StationOptions _station = new() { DeviceIds = [StationId] };
    private bool _backupsEnabled;
    private string _summariesConnectionString = "Server=sql;Database=Weather";
    private CollectorHealth _health = null!;
    private ReceiverHealth _receiver = null!;
    private BackupHealth _backupHealth = null!;
    private WeatherAlertMonitor _monitor = null!;

    public WeatherAlertMonitorTests()
    {
        Directory.CreateDirectory(_dir);
        _archive = new WeatherArchive(Path.Combine(_dir, "weather.db"));
        _archive.InitialiseAsync(CancellationToken.None).GetAwaiter().GetResult();
        Start();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A fresh process: new health state and monitor, same archive and alert state file.</summary>
    private void Start()
    {
        _health = new CollectorHealth(_time);
        _receiver = new ReceiverHealth(_time);
        _backupHealth = new BackupHealth(_time);
        _monitor = new WeatherAlertMonitor(
            _health,
            _receiver,
            _archive,
            new AlertBook(
                _sender,
                new AlertStateStore(Path.Combine(_dir, "alert-state.json"), NullLogger<AlertStateStore>.Instance),
                new CollectorIdentity("WeatherCollector"),
                _time,
                NullLogger<AlertBook>.Instance),
            Options.Create(new AlertOptions()),
            Options.Create(new WeatherAlertOptions()),
            Options.Create(_station),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Weather"] = _summariesConnectionString })
                .Build(),
            new BackupMonitor(
                _backupHealth,
                Options.Create(new BackupOptions { Enabled = _backupsEnabled, AlertAfter = TimeSpan.FromHours(36) }),
                Options.Create(new OffsiteOptions())),
            _time,
            NullLogger<WeatherAlertMonitor>.Instance);
    }

    private async Task RestartAsync()
    {
        Start();
        await _monitor.LoadStateAsync(CancellationToken.None);
    }

    [Fact]
    public async Task No_alert_while_the_station_is_heard()
    {
        for (var i = 0; i < 60; i++)
        {
            await HearAsync(StationId);
            await CheckAfter(TimeSpan.FromMinutes(1));
        }

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Startup_gets_a_grace_period_then_silence_alerts_and_resolves_on_a_message()
    {
        await CheckAfter(TimeSpan.FromMinutes(5));
        Assert.Empty(_sender.Sent);

        await CheckAfter(TimeSpan.FromMinutes(1));
        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[WeatherCollector] PROBLEM: No station messages", problem.Subject);
        Assert.Contains("none since startup", problem.Body);

        await CheckAfter(TimeSpan.FromMinutes(10));
        Assert.Single(_sender.Sent);   // one email per incident

        await HearAsync(StationId);
        await CheckAfter(TimeSpan.FromSeconds(30));
        Assert.Equal("[WeatherCollector] RESOLVED: No station messages", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Restart_during_silence_neither_repeats_nor_resolves_without_a_message()
    {
        await CheckAfter(TimeSpan.FromMinutes(6));
        Assert.Single(_sender.Sent);

        await RestartAsync();
        await CheckAfter(TimeSpan.FromMinutes(1));   // within the new grace period: no evidence either way
        await CheckAfter(TimeSpan.FromMinutes(10));
        Assert.Single(_sender.Sent);

        await HearAsync(StationId);
        await CheckAfter(TimeSpan.FromSeconds(30));
        Assert.Equal("[WeatherCollector] RESOLVED: No station messages", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Missing_dongle_raises_a_receiver_alert_instead_of_silence_and_resolves_when_lines_arrive()
    {
        EndRun(dongleMissing: true);
        await CheckAfter(TimeSpan.FromMinutes(10));

        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[WeatherCollector] PROBLEM: Receiver problem", problem.Subject);
        Assert.Contains("no usable RTL-SDR dongle", problem.Body);

        _receiver.RecordRunStarted();
        _receiver.RecordLine();
        await HearAsync(StationId);
        await CheckAfter(TimeSpan.FromSeconds(30));

        Assert.Equal(2, _sender.Sent.Count);
        Assert.Equal("[WeatherCollector] RESOLVED: Receiver problem", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Repeated_rtl433_restarts_alert_and_resolve_once_an_hour_has_passed_with_lines()
    {
        _receiver.RecordRunStarted();
        _receiver.RecordLine();
        EndRun();
        EndRun();
        await HearAsync(StationId);
        await CheckAfter(TimeSpan.FromSeconds(30));
        Assert.Empty(_sender.Sent);   // two ends: below the threshold

        EndRun();
        await HearAsync(StationId);
        await CheckAfter(TimeSpan.FromSeconds(30));
        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[WeatherCollector] PROBLEM: Receiver problem", problem.Subject);
        Assert.Contains("restarted 3 times in the last hour", problem.Body);

        // Running fine again, but the restarts still count until they're an hour old.
        for (var minute = 0; minute < 61; minute++)
        {
            _receiver.RecordLine();
            await HearAsync(StationId);
            await CheckAfter(TimeSpan.FromMinutes(1));
        }

        Assert.Equal("[WeatherCollector] RESOLVED: Receiver problem", _sender.Sent[^1].Subject);
        Assert.Equal(2, _sender.Sent.Count);
    }

    [Fact]
    public async Task New_id_of_the_station_model_while_the_station_is_silent_raises_station_id_changed()
    {
        await HearAsync(StationId);
        await CheckAfter(TimeSpan.FromMinutes(3));
        await HearAsync("99999999");   // batteries changed: same model, new id

        await CheckAfter(TimeSpan.FromMinutes(3));
        var problem = Assert.Single(_sender.Sent);   // and no separate "No station messages"
        Assert.Equal("[WeatherCollector] PROBLEM: Station id changed", problem.Subject);
        Assert.Contains("id 99999999", problem.Body);
        Assert.Contains("Station__DeviceIds__1=<id>", problem.Body);

        // The user adds the new id and restarts: the station is heard again.
        _station = new StationOptions { DeviceIds = [StationId, "99999999"] };
        await RestartAsync();
        await HearAsync("99999999");
        await CheckAfter(TimeSpan.FromSeconds(30));
        Assert.Equal("[WeatherCollector] RESOLVED: Station id changed", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task A_neighbour_heard_all_along_is_not_a_new_station_id()
    {
        await HearAsync("87654321");
        await HearAsync(StationId);
        await CheckAfter(TimeSpan.FromMinutes(3));
        await HearAsync("87654321");

        await CheckAfter(TimeSpan.FromMinutes(3));
        Assert.Equal("[WeatherCollector] PROBLEM: No station messages", Assert.Single(_sender.Sent).Subject);
    }

    [Fact]
    public async Task Another_model_is_not_a_new_station_id()
    {
        await HearAsync(StationId);
        await CheckAfter(TimeSpan.FromMinutes(3));
        await HearAsync("99999999", model: "Bresser-5in1");

        await CheckAfter(TimeSpan.FromMinutes(3));
        Assert.Equal("[WeatherCollector] PROBLEM: No station messages", Assert.Single(_sender.Sent).Subject);
    }

    [Fact]
    public async Task Battery_low_for_an_hour_alerts_and_resolves_when_it_reports_ok()
    {
        for (var minute = 0; minute < 59; minute++)
        {
            await HearAsync(StationId, batteryOk: 0);
            await HearAsync(StationId, batteryOk: null);   // rain messages carry no battery flag
            await CheckAfter(TimeSpan.FromMinutes(1));
        }

        Assert.Empty(_sender.Sent);

        await HearAsync(StationId, batteryOk: 0);
        await CheckAfter(TimeSpan.FromMinutes(1));
        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[WeatherCollector] PROBLEM: Sensor battery low", problem.Subject);
        Assert.Contains("Station battery: LOW", problem.Body);

        await HearAsync(StationId, batteryOk: 1);
        await CheckAfter(TimeSpan.FromSeconds(30));
        Assert.Equal("[WeatherCollector] RESOLVED: Sensor battery low", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Battery_alert_survives_a_restart_until_the_battery_reports_ok()
    {
        for (var minute = 0; minute <= 60; minute++)
        {
            await HearAsync(StationId, batteryOk: 0);
            await CheckAfter(TimeSpan.FromMinutes(1));
        }

        Assert.Single(_sender.Sent);

        await RestartAsync();
        await HearAsync(StationId, batteryOk: null);
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Single(_sender.Sent);

        await HearAsync(StationId, batteryOk: 1);
        await CheckAfter(TimeSpan.FromSeconds(30));
        Assert.Equal("[WeatherCollector] RESOLVED: Sensor battery low", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Without_configured_station_ids_there_are_no_station_alerts()
    {
        _station = new StationOptions();
        Start();

        await HearAsync(StationId, batteryOk: 0);
        await CheckAfter(TimeSpan.FromHours(2));

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Summaries_failing_for_half_an_hour_alert_and_resolve_when_publishing_succeeds()
    {
        await HearAsync(StationId, batteryOk: 1);
        _health.RecordPublishSuccess();
        _health.RecordPublishFailure(new InvalidOperationException("SQL Server is down"));
        for (var minute = 0; minute < 30; minute++)
        {
            await HearAsync(StationId, batteryOk: 1);
            await CheckAfter(TimeSpan.FromMinutes(1));
        }

        Assert.Empty(_sender.Sent);

        await HearAsync(StationId, batteryOk: 1);
        await CheckAfter(TimeSpan.FromMinutes(1));
        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[WeatherCollector] PROBLEM: Summaries for Grafana stalled", problem.Subject);
        Assert.Contains("InvalidOperationException: SQL Server is down", problem.Body);

        _health.RecordPublishSuccess();
        await CheckAfter(TimeSpan.FromSeconds(30));
        Assert.Equal("[WeatherCollector] RESOLVED: Summaries for Grafana stalled", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Turning_summaries_off_resolves_their_alert()
    {
        _health.RecordPublishFailure(new InvalidOperationException("SQL Server is down"));
        await CheckAfter(TimeSpan.FromMinutes(31));
        Assert.Contains(_sender.Sent, m => m.Subject == "[WeatherCollector] PROBLEM: Summaries for Grafana stalled");

        _summariesConnectionString = "";
        await RestartAsync();
        await CheckAfter(TimeSpan.FromSeconds(30));

        Assert.Equal("[WeatherCollector] RESOLVED: Summaries for Grafana stalled", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Backups_are_checked_too()
    {
        _backupsEnabled = true;
        Start();

        for (var hour = 0; hour < 37; hour++)
        {
            _time.Advance(TimeSpan.FromHours(1));
            await HearAsync(StationId);
            await _monitor.CheckAsync(CancellationToken.None);
        }

        Assert.Equal("[WeatherCollector] PROBLEM: Backups failing", Assert.Single(_sender.Sent).Subject);

        _backupHealth.RecordBackup(_time.GetUtcNow());
        await HearAsync(StationId);
        await CheckAfter(TimeSpan.FromSeconds(30));
        Assert.Equal("[WeatherCollector] RESOLVED: Backups failing", _sender.Sent[^1].Subject);
    }

    /// <summary>Archives a message heard now, and records it in health if it's the station's (as <see cref="CollectorService"/> does).</summary>
    private async Task HearAsync(string id, int? batteryOk = 1, string model = "Bresser-6in1")
    {
        var now = _time.GetUtcNow();
        var battery = batteryOk is { } ok ? $"\"battery_ok\" : {ok}, " : "";
        var message = Rtl433Message.Parse($$"""{"model" : "{{model}}", "id" : {{id}}, {{battery}}"wind_avg_m_s" : 1.2}""");
        var isStation = _station.IsStation(message.Model, message.DeviceId);
        await _archive.AppendAsync(message, now, isStation, CancellationToken.None);
        if (isStation)
        {
            _health.RecordStationMessage(now, id, message.Weather!);
        }
    }

    private void EndRun(bool dongleMissing = false)
    {
        var now = _time.GetUtcNow();
        _receiver.RecordRunEnded(new RunResult(RunEnd.Exited, 1, dongleMissing, 0, now, now));
    }

    private async Task CheckAfter(TimeSpan delay)
    {
        _time.Advance(delay);
        await _monitor.CheckAsync(CancellationToken.None);
    }

    private sealed class RecordingSender : IAlertSender
    {
        public List<(string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string subject, string body, CancellationToken cancellationToken)
        {
            Sent.Add((subject, body));
            return Task.CompletedTask;
        }
    }
}
