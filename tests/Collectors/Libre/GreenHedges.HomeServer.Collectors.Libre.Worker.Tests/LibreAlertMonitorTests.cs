using System.Net;
using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;
using GreenHedges.HomeServer.Collectors.Libre.Nightscout;
using GreenHedges.HomeServer.Collectors.Libre.Worker.Alerts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker.Tests;

public sealed class LibreAlertMonitorTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingSender _sender = new();
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"librecollector-alerts-{Guid.NewGuid():N}.json");
    private readonly bool _backupsEnabled;
    private readonly bool _offsiteEnabled;
    private CollectorHealth _health = null!;
    private BackupHealth _backupHealth = null!;
    private LibreAlertMonitor _monitor = null!;

    public LibreAlertMonitorTests()
        : this(backupsEnabled: false)
    {
    }

    private LibreAlertMonitorTests(bool backupsEnabled, bool offsiteEnabled = false)
    {
        _backupsEnabled = backupsEnabled;
        _offsiteEnabled = offsiteEnabled;
        Start();
    }

    public void Dispose() => File.Delete(_statePath);

    /// <summary>A fresh process: new health state and monitor, same alert state file.</summary>
    private void Start()
    {
        _health = new CollectorHealth(_time);
        _backupHealth = new BackupHealth(_time);
        _monitor = new LibreAlertMonitor(
            _health,
            new AlertBook(
                _sender,
                new AlertStateStore(_statePath, NullLogger<AlertStateStore>.Instance),
                new CollectorIdentity("LibreCollector"),
                _time,
                NullLogger<AlertBook>.Instance),
            Options.Create(new AlertOptions()),
            Options.Create(new LibreAlertOptions { StaleAfter = TimeSpan.FromMinutes(20) }),
            Options.Create(new NightscoutOptions { BaseUrl = "http://127.0.0.1:1337" }),
            new BackupMonitor(
                _backupHealth,
                Options.Create(new BackupOptions { Enabled = _backupsEnabled, AlertAfter = TimeSpan.FromHours(36) }),
                Options.Create(new OffsiteOptions { Bucket = _offsiteEnabled ? "bucket" : null, AlertAfter = TimeSpan.FromHours(36) })),
            _time,
            NullLogger<LibreAlertMonitor>.Instance);
    }

    private async Task RestartAsync()
    {
        Start();
        await _monitor.LoadStateAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Offsite_alert_after_36_hours_without_a_copy_then_resolves()
    {
        var tests = new LibreAlertMonitorTests(backupsEnabled: true, offsiteEnabled: true);
        tests._backupHealth.RecordOffsite(tests._time.GetUtcNow());

        for (var hour = 1; hour <= 37; hour++)
        {
            tests._time.Advance(TimeSpan.FromHours(1));
            tests._health.RecordReading(tests._time.GetUtcNow());
            tests._backupHealth.RecordBackup(tests._time.GetUtcNow());   // local backups fine; only off-site failing
            if (hour == 30)
            {
                tests._backupHealth.RecordOffsiteFailure(new InvalidOperationException("AccessDenied"));
            }

            await tests._monitor.CheckAsync(CancellationToken.None);
        }

        var problem = Assert.Single(tests._sender.Sent);
        Assert.Equal("[LibreCollector] PROBLEM: Off-site backup failing", problem.Subject);
        Assert.Contains("AccessDenied", problem.Body);

        tests._backupHealth.RecordOffsite(tests._time.GetUtcNow());
        await tests._monitor.CheckAsync(CancellationToken.None);
        Assert.Equal("[LibreCollector] RESOLVED: Off-site backup failing", tests._sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Offsite_misconfiguration_alerts_immediately()
    {
        var tests = new LibreAlertMonitorTests(backupsEnabled: true, offsiteEnabled: true);
        tests._backupHealth.RecordBackup(tests._time.GetUtcNow());
        tests._backupHealth.RecordOffsiteFailure(new OffsiteMisconfiguredException("Offsite:Region is not set."));

        tests._health.RecordReading(tests._time.GetUtcNow());
        await tests._monitor.CheckAsync(CancellationToken.None);

        var problem = Assert.Single(tests._sender.Sent);
        Assert.Equal("[LibreCollector] PROBLEM: Off-site backup failing", problem.Subject);
        Assert.Contains("Offsite:Region is not set.", problem.Body);
    }

    [Fact]
    public async Task Restart_during_stale_incident_sends_no_second_problem_and_resolves_on_a_real_reading()
    {
        await CheckAfter(TimeSpan.FromMinutes(21));
        Assert.Equal("[LibreCollector] PROBLEM: No new glucose readings", Assert.Single(_sender.Sent).Subject);
        await CheckAfter(TimeSpan.FromMinutes(10)); // incident already 10 min old when the process restarts

        await RestartAsync();

        // Inside the new process's startup grace, with no readings yet: must not count as resolved.
        await CheckAfter(TimeSpan.FromMinutes(1));
        // Still no data well past the grace period: must not send a second PROBLEM.
        await CheckAfter(TimeSpan.FromMinutes(30));
        Assert.Single(_sender.Sent);

        _health.RecordReading(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));

        var resolved = _sender.Sent[^1];
        Assert.Equal("[LibreCollector] RESOLVED: No new glucose readings", resolved.Subject);
        Assert.Contains("Resolved after 42m", resolved.Body); // from the original start (minute 21), not the restart (minute 31)
    }

    [Fact]
    public async Task Restart_during_user_action_incident_waits_for_a_successful_poll()
    {
        _health.RecordFailure(new BadCredentialsException(2, HttpStatusCode.OK));
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Single(_sender.Sent);

        await RestartAsync();
        await CheckAfter(TimeSpan.FromMinutes(1));   // no error seen yet in the new process, but no success either
        Assert.Single(_sender.Sent);

        _health.RecordSuccess();
        _health.RecordReading(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Equal("[LibreCollector] RESOLVED: LibreLinkUp needs attention", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Restart_during_publish_incident_waits_for_a_successful_publish()
    {
        _health.RecordPublishFailure(new HttpRequestException("Connection refused"));
        for (var minute = 0; minute < 31; minute++)
        {
            _health.RecordReading(_time.GetUtcNow());
            await CheckAfter(TimeSpan.FromMinutes(1));
        }

        Assert.Single(_sender.Sent);

        await RestartAsync();
        _health.RecordReading(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Single(_sender.Sent);

        _health.RecordPublishSuccess();
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Equal("[LibreCollector] RESOLVED: Nightscout publishing stalled", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Alert_whose_email_never_went_out_is_still_retried_after_restart()
    {
        _sender.FailuresRemaining = int.MaxValue;
        await CheckAfter(TimeSpan.FromMinutes(21));
        Assert.Empty(_sender.Sent);

        await RestartAsync();
        _sender.FailuresRemaining = 0;
        await CheckAfter(TimeSpan.FromMinutes(21)); // still stale (no readings) after the new grace period

        Assert.Equal("[LibreCollector] PROBLEM: No new glucose readings", Assert.Single(_sender.Sent).Subject);
    }

    [Fact]
    public async Task Resolved_alerts_are_removed_from_the_state_file()
    {
        await CheckAfter(TimeSpan.FromMinutes(21));
        Assert.Contains("Stale", await File.ReadAllTextAsync(_statePath));

        _health.RecordReading(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));

        await RestartAsync();
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Equal(2, _sender.Sent.Count); // PROBLEM + RESOLVED only; nothing replayed after restart
        Assert.DoesNotContain("Stale", await File.ReadAllTextAsync(_statePath));
    }

    [Fact]
    public async Task Unreadable_state_file_means_no_open_alerts()
    {
        await File.WriteAllTextAsync(_statePath, "{ not json");

        await RestartAsync();
        await CheckAfter(TimeSpan.FromMinutes(1));

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Backup_alert_after_36_hours_without_a_good_backup_then_resolves()
    {
        var tests = new LibreAlertMonitorTests(backupsEnabled: true);
        tests._backupHealth.RecordBackup(tests._time.GetUtcNow());

        // Keep readings fresh so only the backup alert can fire.
        for (var hour = 1; hour <= 36; hour++)
        {
            tests._time.Advance(TimeSpan.FromHours(1));
            tests._health.RecordReading(tests._time.GetUtcNow());
            await tests._monitor.CheckAsync(CancellationToken.None);
        }

        Assert.Empty(tests._sender.Sent);

        tests._backupHealth.RecordBackupFailure(new IOException("Disk full"));
        tests._time.Advance(TimeSpan.FromMinutes(1));
        tests._health.RecordReading(tests._time.GetUtcNow());
        await tests._monitor.CheckAsync(CancellationToken.None);

        var problem = Assert.Single(tests._sender.Sent);
        Assert.Equal("[LibreCollector] PROBLEM: Backups failing", problem.Subject);
        Assert.Contains("IOException: Disk full", problem.Body);

        tests._backupHealth.RecordBackup(tests._time.GetUtcNow());
        await tests._monitor.CheckAsync(CancellationToken.None);

        Assert.Equal("[LibreCollector] RESOLVED: Backups failing", tests._sender.Sent[^1].Subject);
    }

    private Task CheckAfter(TimeSpan elapsed)
    {
        _time.Advance(elapsed);
        return _monitor.CheckAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Publish_stalled_alert_after_30_minutes_of_failures_then_resolves()
    {
        _health.RecordPublishFailure(new HttpRequestException("Connection refused (127.0.0.1:1337)"));

        for (var minute = 0; minute < 30; minute++)
        {
            _health.RecordReading(_time.GetUtcNow());
            await CheckAfter(TimeSpan.FromMinutes(1));
        }

        Assert.Empty(_sender.Sent);

        _health.RecordReading(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));

        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[LibreCollector] PROBLEM: Nightscout publishing stalled", problem.Subject);
        Assert.Contains("Connection refused", problem.Body);

        _health.RecordPublishSuccess();
        await CheckAfter(TimeSpan.FromMinutes(1));

        Assert.Equal("[LibreCollector] RESOLVED: Nightscout publishing stalled", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task No_alert_during_startup_grace_period()
    {
        await CheckAfter(TimeSpan.FromMinutes(19));

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Regular_readings_keep_it_quiet()
    {
        for (var i = 0; i < 12; i++)
        {
            _health.RecordReading(_time.GetUtcNow());
            await CheckAfter(TimeSpan.FromMinutes(5));
        }

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Stale_data_alerts_once_then_resolves()
    {
        _health.RecordReading(_time.GetUtcNow().AddMinutes(1));

        await CheckAfter(TimeSpan.FromMinutes(22));
        await CheckAfter(TimeSpan.FromMinutes(1));
        await CheckAfter(TimeSpan.FromMinutes(30));

        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[LibreCollector] PROBLEM: No new glucose readings", problem.Subject);
        Assert.Contains("No new glucose reading for 21m", problem.Body);

        _health.RecordReading(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));

        Assert.Equal(2, _sender.Sent.Count);
        Assert.Equal("[LibreCollector] RESOLVED: No new glucose readings", _sender.Sent[1].Subject);
        Assert.Contains("Resolved after 32m", _sender.Sent[1].Body);
    }

    [Fact]
    public async Task User_action_alerts_immediately_and_suppresses_stale_alert()
    {
        _health.RecordFailure(new BadCredentialsException(2, HttpStatusCode.OK));

        await CheckAfter(TimeSpan.FromMinutes(1));
        await CheckAfter(TimeSpan.FromMinutes(45));

        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[LibreCollector] PROBLEM: LibreLinkUp needs attention", problem.Subject);
        Assert.Contains("rejected the email or password", problem.Body);

        _health.RecordSuccess();
        _health.RecordReading(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));

        Assert.Equal(2, _sender.Sent.Count);
        Assert.Equal("[LibreCollector] RESOLVED: LibreLinkUp needs attention", _sender.Sent[1].Subject);
    }

    [Fact]
    public async Task Stale_alert_includes_last_error()
    {
        _health.RecordFailure(new HttpRequestException("Connection refused"));

        await CheckAfter(TimeSpan.FromMinutes(21));

        var problem = Assert.Single(_sender.Sent);
        Assert.Contains("HttpRequestException: Connection refused", problem.Body);
    }

    [Fact]
    public async Task Failed_send_is_retried_on_next_check()
    {
        _sender.FailuresRemaining = 1;

        await CheckAfter(TimeSpan.FromMinutes(21));
        Assert.Empty(_sender.Sent);

        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task No_resolved_email_if_problem_email_never_went_out()
    {
        _sender.FailuresRemaining = int.MaxValue;
        await CheckAfter(TimeSpan.FromMinutes(21));

        _sender.FailuresRemaining = 0;
        _health.RecordReading(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));

        Assert.Empty(_sender.Sent);
    }

    /// <summary>A sensor swap like the real one: the old sensor's last reading, then activation 27 s later, 60 min warm-up.</summary>
    private DateTimeOffset SwapSensor(string serial = "NEW")
    {
        _health.RecordReading(_time.GetUtcNow());
        _time.Advance(TimeSpan.FromSeconds(27));
        var activated = _time.GetUtcNow();
        _health.RecordSensor(new SensorStatus(serial, activated, activated.AddMinutes(60), activated.AddDays(15)));
        return activated;
    }

    [Fact]
    public async Task No_stale_alert_during_a_new_sensors_warm_up()
    {
        var activated = SwapSensor();

        // Checked every minute to 19 minutes after warm-up is due to end.
        while (_time.GetUtcNow() < activated.AddMinutes(60 + 19))
        {
            await CheckAfter(TimeSpan.FromMinutes(1));
        }

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Stale_alert_if_warm_up_overruns_says_when_readings_were_expected()
    {
        var activated = SwapSensor();

        _time.Advance(TimeSpan.FromMinutes(60 + 21));
        await _monitor.CheckAsync(CancellationToken.None);

        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[LibreCollector] PROBLEM: No new glucose readings", problem.Subject);
        Assert.Contains("No new glucose reading for 1h 21m.", problem.Body);
        Assert.Contains($"New sensor NEW was activated {activated:u}; its readings were expected from {activated.AddMinutes(60):u}.", problem.Body);
        Assert.Contains("Sensor: NEW, activated", problem.Body);
    }

    [Fact]
    public async Task Warm_up_hold_survives_a_restart()
    {
        var activated = SwapSensor();
        await CheckAfter(TimeSpan.FromMinutes(10));

        await RestartAsync(); // no readings in the new process; the tracker reloads the sensor at startup
        _health.RecordSensor(new SensorStatus("NEW", activated, activated.AddMinutes(60), activated.AddDays(15)));
        await CheckAfter(TimeSpan.FromMinutes(60 + 19 - 10));

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Readings_from_the_new_sensor_end_the_hold()
    {
        var activated = SwapSensor();
        _time.Advance(TimeSpan.FromMinutes(61));
        _health.RecordReading(_time.GetUtcNow());   // first reading from the new sensor

        await CheckAfter(TimeSpan.FromMinutes(21)); // then nothing: an ordinary stale alert

        var problem = Assert.Single(_sender.Sent);
        Assert.Contains("No new glucose reading for 21m.", problem.Body);
        Assert.DoesNotContain("was activated", problem.Body);
        Assert.True(activated < _time.GetUtcNow());
    }

    [Fact]
    public async Task Stale_alert_open_before_a_swap_stays_open_until_a_reading()
    {
        await CheckAfter(TimeSpan.FromMinutes(21));
        Assert.Single(_sender.Sent);

        await CheckAfter(TimeSpan.FromHours(3));    // sensor failed; replaced hours later
        var activated = _time.GetUtcNow();
        _health.RecordSensor(new SensorStatus("NEW", activated, activated.AddMinutes(60), activated.AddDays(15)));
        await CheckAfter(TimeSpan.FromMinutes(30)); // mid-warm-up: neither a second PROBLEM nor a RESOLVED
        Assert.Single(_sender.Sent);

        _time.Advance(TimeSpan.FromMinutes(31));
        _health.RecordReading(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Equal("[LibreCollector] RESOLVED: No new glucose readings", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Stale_alert_after_the_sensors_expected_end_says_it_has_probably_expired()
    {
        var activated = _time.GetUtcNow().AddDays(-15).AddMinutes(-5);
        _health.RecordSensor(new SensorStatus("OLD", activated, activated.AddMinutes(60), activated.AddDays(15)));
        await CheckAfter(TimeSpan.FromMinutes(21));

        var problem = Assert.Single(_sender.Sent);
        Assert.Contains($"Sensor OLD reached its expected end at {activated.AddDays(15):u}; it has probably expired.", problem.Body);
        Assert.DoesNotContain("was activated", problem.Body);
    }

    private sealed class RecordingSender : IAlertSender
    {
        public List<(string Subject, string Body)> Sent { get; } = [];

        public int FailuresRemaining { get; set; }

        public Task SendAsync(string subject, string body, CancellationToken cancellationToken)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new HttpRequestException("Resend is down");
            }

            Sent.Add((subject, body));
            return Task.CompletedTask;
        }
    }
}
