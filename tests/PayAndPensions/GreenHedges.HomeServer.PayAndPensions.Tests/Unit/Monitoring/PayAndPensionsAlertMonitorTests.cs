using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Common.Hosting;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Monitoring;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit.Monitoring;

public sealed class PayAndPensionsAlertMonitorTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingSender _sender = new();
    private readonly FakeProbe _database = new();
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"payandpensions-alerts-{Guid.NewGuid():N}.json");
    private readonly AppHealth _health;
    private readonly BackupHealth _backupHealth;
    private readonly PayAndPensionsAlertMonitor _monitor;

    public PayAndPensionsAlertMonitorTests()
    {
        _health = new AppHealth(_time);
        _backupHealth = new BackupHealth(_time);
        _monitor = new PayAndPensionsAlertMonitor(
            _health,
            _database,
            new BackupMonitor(_backupHealth, Options.Create(new BackupOptions { Enabled = true }), Options.Create(new OffsiteOptions())),
            new AlertBook(_sender, new AlertStateStore(_statePath, NullLogger<AlertStateStore>.Instance),
                new CollectorIdentity("PayAndPensions"), _time, NullLogger<AlertBook>.Instance),
            Options.Create(new AlertOptions()),
            Options.Create(new PayAndPensionsAlertOptions()),
            _time,
            NullLogger<PayAndPensionsAlertMonitor>.Instance);
    }

    public void Dispose() => File.Delete(_statePath);

    private async Task CheckAfter(TimeSpan delay)
    {
        _time.Advance(delay);
        await _monitor.CheckAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Database_down_for_five_minutes_alerts_once_and_resolves_when_it_answers()
    {
        _backupHealth.RecordBackup(_time.GetUtcNow());
        _database.Problem = "Connection refused";
        for (var minute = 1; minute <= 4; minute++) await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Empty(_sender.Sent);

        await CheckAfter(TimeSpan.FromMinutes(2));
        Assert.Equal("[PayAndPensions] PROBLEM: Database unreachable", Assert.Single(_sender.Sent).Subject);

        _database.Problem = null;
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Equal("[PayAndPensions] RESOLVED: Database unreachable", _sender.Sent[^1].Subject);
    }

    [Fact]
    public async Task Database_backups_are_checked_too()
    {
        for (var hour = 0; hour < 37; hour++) await CheckAfter(TimeSpan.FromHours(1));
        Assert.Equal("[PayAndPensions] PROBLEM: Backups failing", Assert.Single(_sender.Sent).Subject);

        _backupHealth.RecordBackup(_time.GetUtcNow());
        await CheckAfter(TimeSpan.FromMinutes(1));
        Assert.Equal("[PayAndPensions] RESOLVED: Backups failing", _sender.Sent[^1].Subject);
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
