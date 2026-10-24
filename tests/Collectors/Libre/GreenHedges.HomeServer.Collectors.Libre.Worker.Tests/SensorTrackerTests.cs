using GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;
using GreenHedges.HomeServer.Collectors.Libre.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker.Tests;

public sealed class SensorTrackerTests : IDisposable
{
    private static readonly DateTimeOffset Activated = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"librecollector-sensor-test-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero));
    private readonly GlucoseArchive _archive;
    private readonly ListLogger _logger = new();
    private readonly CollectorHealth _health;

    public SensorTrackerTests()
    {
        _archive = new GlucoseArchive(_path);
        _health = new CollectorHealth(_time);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }

    private SensorTracker Tracker() =>
        new(_archive, _health, Options.Create(new CollectorOptions()), _time, _logger);

    private static SensorInfo Sensor(string serial, DateTimeOffset activated, int? warmupMinutes = 60) =>
        new(serial, activated, warmupMinutes, 4, $$"""{"sn":"{{serial}}"}""");

    [Fact]
    public async Task First_sensor_then_unchanged_then_changed()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        var tracker = Tracker();

        Assert.Equal(SensorObservation.FirstSeen, await tracker.ObserveAsync(Sensor("SN1", Activated), CancellationToken.None));
        Assert.Equal(SensorObservation.Unchanged, await tracker.ObserveAsync(Sensor("SN1", Activated), CancellationToken.None));
        Assert.Equal(SensorObservation.None, await tracker.ObserveAsync(null, CancellationToken.None));

        _time.Advance(TimeSpan.FromDays(8));
        var newActivation = Activated.AddDays(15);
        Assert.Equal(SensorObservation.Changed, await tracker.ObserveAsync(Sensor("SN2", newActivation), CancellationToken.None));

        var changed = Assert.Single(_logger.Messages, m => m.StartsWith("Sensor changed"));
        Assert.Contains("SN1 -> SN2", changed);
        Assert.Contains("readings expected from 2026-10-25 09:00:00Z", changed); // activation + 60 min warm-up
        Assert.Contains("expected end 2026-11-09 08:00:00Z", changed);           // activation + 15 days
    }

    [Fact]
    public async Task Restart_with_the_same_sensor_is_not_a_change()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await Tracker().ObserveAsync(Sensor("SN1", Activated), CancellationToken.None);

        var afterRestart = Tracker();
        await afterRestart.LogCurrentAsync(CancellationToken.None);

        Assert.Equal(SensorObservation.Unchanged, await afterRestart.ObserveAsync(Sensor("SN1", Activated), CancellationToken.None));
        Assert.Contains(_logger.Messages, m => m.StartsWith("Current sensor SN1") && m.Contains("expected end 2026-10-25 08:00:00Z") && m.Contains("(7d 22h left)"));
    }

    [Fact]
    public async Task Restart_onto_a_new_sensor_is_a_change()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await Tracker().ObserveAsync(Sensor("SN1", Activated), CancellationToken.None);

        Assert.Equal(SensorObservation.Changed, await Tracker().ObserveAsync(Sensor("SN2", Activated.AddDays(15)), CancellationToken.None));
    }

    [Fact]
    public async Task Last_reading_of_the_old_sensor_keeps_its_serial_when_it_arrives_with_the_new_sensor()
    {
        // As on a real swap: the old sensor's last reading (27 s before the new activation) came in the
        // same poll that first reported the new sensor.
        await _archive.InitialiseAsync(CancellationToken.None);
        var tracker = Tracker();
        await tracker.ObserveAsync(Sensor("OLD", Activated), CancellationToken.None);

        var newActivation = Activated.AddDays(15);
        await tracker.ObserveAsync(Sensor("NEW", newActivation), CancellationToken.None);

        Assert.Equal("OLD", tracker.SerialAt(newActivation.AddSeconds(-27)));
        Assert.Equal("NEW", tracker.SerialAt(newActivation.AddMinutes(61)));
    }

    [Fact]
    public async Task Previous_sensor_is_remembered_across_a_restart()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        var newActivation = Activated.AddDays(15);
        await Tracker().ObserveAsync(Sensor("OLD", Activated), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(1));
        await Tracker().ObserveAsync(Sensor("NEW", newActivation), CancellationToken.None);

        var afterRestart = Tracker();
        await afterRestart.LogCurrentAsync(CancellationToken.None);

        Assert.Equal("OLD", afterRestart.SerialAt(newActivation.AddSeconds(-27)));
        Assert.Equal("NEW", afterRestart.SerialAt(newActivation));
    }

    [Fact]
    public async Task Reading_before_the_only_known_sensor_has_no_serial()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        var tracker = Tracker();
        await tracker.ObserveAsync(Sensor("SN1", Activated), CancellationToken.None);

        Assert.Null(tracker.SerialAt(Activated.AddMinutes(-5)));
        Assert.Equal("SN1", tracker.SerialAt(Activated.AddMinutes(5)));
    }

    [Fact]
    public async Task Sensor_status_is_passed_to_health_with_its_warm_up()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await Tracker().ObserveAsync(Sensor("SN1", Activated), CancellationToken.None);

        Assert.Equal(
            new SensorStatus("SN1", Activated, Activated.AddMinutes(60), Activated.AddDays(15)),
            _health.Snapshot().Sensor);

        await Tracker().ObserveAsync(Sensor("SN2", Activated.AddDays(15), warmupMinutes: null), CancellationToken.None);
        Assert.Equal(Activated.AddDays(15).AddMinutes(60), _health.Snapshot().Sensor?.ReadyAt); // SensorWarmup default
    }

    [Fact]
    public async Task Startup_passes_the_archived_sensor_to_health()
    {
        await _archive.InitialiseAsync(CancellationToken.None);
        await Tracker().ObserveAsync(Sensor("SN1", Activated, warmupMinutes: 45), CancellationToken.None);

        var health = new CollectorHealth(_time);
        await new SensorTracker(_archive, health, Options.Create(new CollectorOptions()), _time, _logger)
            .LogCurrentAsync(CancellationToken.None);

        Assert.Equal(Activated.AddMinutes(45), health.Snapshot().Sensor?.ReadyAt);
    }

    private sealed class ListLogger : ILogger<SensorTracker>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
