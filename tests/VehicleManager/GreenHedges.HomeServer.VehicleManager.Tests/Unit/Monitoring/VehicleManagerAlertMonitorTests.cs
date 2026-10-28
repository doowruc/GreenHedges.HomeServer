using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Monitoring;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit.Monitoring;

public sealed class VehicleManagerAlertMonitorTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingSender _sender = new();
    private readonly FakeProbe _database = new();
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"vehiclemanager-alerts-{Guid.NewGuid():N}.json");
    private bool _backupsEnabled;
    private AppHealth _health = null!;
    private BackupHealth _backupHealth = null!;
    private VehicleManagerAlertMonitor _monitor = null!;

    public VehicleManagerAlertMonitorTests() => Start();

    public void Dispose() => File.Delete(_statePath);

    /// <summary>A fresh process: new health and monitor, same alert state file.</summary>
    private void Start()
    {
        _health = new AppHealth(_time);
        _backupHealth = new BackupHealth(_time);
        _monitor = new VehicleManagerAlertMonitor(
            _health,
            _database,
            new BackupMonitor(
                _backupHealth,
                Options.Create(new BackupOptions { Enabled = _backupsEnabled }),
                Options.Create(new OffsiteOptions())),
            new AlertBook(
                _sender,
                new AlertStateStore(_statePath, NullLogger<AlertStateStore>.Instance),
                new CollectorIdentity("VehicleManager"),
                _time,
                NullLogger<AlertBook>.Instance),
            Options.Create(new AlertOptions()),
            Options.Create(new VehicleManagerAlertOptions()),
            _time,
            NullLogger<VehicleManagerAlertMonitor>.Instance);
    }

    private async Task RestartAsync()
    {
        Start();
        await _monitor.LoadStateAsync(CancellationToken.None);
    }

    private async Task CheckAfter(TimeSpan delay)
    {
        _time.Advance(delay);
        await _monitor.CheckAsync(CancellationToken.None);
    }

    private void Run(ScheduledJob job, int users = 2, int failures = 0) =>
        _health.RecordRun(job, users, failures, failures > 0 ? new InvalidOperationException("Resend said no") : null);

    [Fact]
    public async Task Database_down_for_five_minutes_alerts_once_and_resolves_when_it_answers()
    {
        _database.Problem = "Connection refused";
        for (var minute = 1; minute <= 4; minute++) await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Empty(_sender.Sent); // failing since the first check: 3 minutes so far

        await CheckAfter(TimeSpan.FromMinutes(2));
        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[VehicleManager] PROBLEM: Database unreachable", problem.Subject);
        Assert.Contains("Connection refused", problem.Body);

        await CheckAfter(TimeSpan.FromMinutes(10));
        Assert.Single(_sender.Sent);

        _database.Problem = null;
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Equal("[VehicleManager] RESOLVED: Database unreachable", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task A_run_with_failures_alerts_and_a_clean_run_resolves_it()
    {
        Run(ScheduledJob.NightlyBackups, users: 3, failures: 1);
        await CheckAfter(TimeSpan.FromMinutes(1));

        var problem = Assert.Single(_sender.Sent);
        Assert.Equal("[VehicleManager] PROBLEM: Nightly backups failing", problem.Subject);
        Assert.Contains("failed for 1 of 3 users", problem.Body);
        Assert.Contains("InvalidOperationException: Resend said no", problem.Body);

        Run(ScheduledJob.NightlyBackups);
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Equal("[VehicleManager] RESOLVED: Nightly backups failing", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task A_job_that_hasnt_completed_for_26_hours_alerts()
    {
        await CheckAfter(TimeSpan.FromHours(26));
        Assert.Empty(_sender.Sent); // a grace period from startup

        Run(ScheduledJob.NightlyBackups);
        Run(ScheduledJob.MorningReminders);
        await CheckAfter(TimeSpan.FromMinutes(2));
        Assert.Empty(_sender.Sent);

        await CheckAfter(TimeSpan.FromHours(26));
        Assert.Equal(2, _sender.Sent.Count);
        var problem = Assert.Single(_sender.Sent, e => e.Subject == "[VehicleManager] PROBLEM: Nightly backups failing");
        Assert.Contains("No run has completed for 26h", problem.Body);
        Assert.Contains(_sender.Sent, e => e.Subject == "[VehicleManager] PROBLEM: Morning reminders failing");
    }

    [Fact]
    public async Task Job_alerts_wait_while_the_database_is_down()
    {
        _database.Problem = "Connection refused";
        Run(ScheduledJob.MorningReminders, users: 1, failures: 1);
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Empty(_sender.Sent); // the database is failing, even before its own alert is due
        await CheckAfter(TimeSpan.FromMinutes(5));
        Assert.Equal(["[VehicleManager] PROBLEM: Database unreachable"], _sender.Sent.Select(e => e.Subject));

        _database.Problem = null;
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Contains(_sender.Sent, e => e.Subject == "[VehicleManager] PROBLEM: Morning reminders failing");
    }

    [Fact]
    public async Task A_restart_doesnt_resolve_without_evidence_or_repeat_the_email()
    {
        Run(ScheduledJob.NightlyBackups, users: 1, failures: 1);
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Single(_sender.Sent);

        await RestartAsync();
        await CheckAfter(TimeSpan.FromMinutes(1));
        await CheckAfter(TimeSpan.FromHours(1));
        Assert.Single(_sender.Sent);

        Run(ScheduledJob.NightlyBackups);
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Equal("[VehicleManager] RESOLVED: Nightly backups failing", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Database_backups_are_checked_too()
    {
        _backupsEnabled = true;
        Start();
        for (var hour = 0; hour < 37; hour++)
        {
            Run(ScheduledJob.NightlyBackups);
            Run(ScheduledJob.MorningReminders);
            await CheckAfter(TimeSpan.FromHours(1));
        }

        Assert.Equal("[VehicleManager] PROBLEM: Backups failing", Assert.Single(_sender.Sent).Subject);

        _backupHealth.RecordBackup(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Equal("[VehicleManager] RESOLVED: Backups failing", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task The_heartbeat_is_healthy_only_while_the_database_answers()
    {
        var interval = TimeSpan.FromMinutes(5);
        Assert.Null(_health.HealthyStatus(_time.GetUtcNow(), interval));

        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.NotNull(_health.HealthyStatus(_time.GetUtcNow(), interval));

        _database.Problem = "Connection refused";
        await CheckAfter(TimeSpan.FromMinutes(6));
        Assert.Null(_health.HealthyStatus(_time.GetUtcNow(), interval));
    }

    private sealed class FakeProbe : IDatabaseProbe
    {
        public string? Problem { get; set; }

        public Task<string?> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(Problem);
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
