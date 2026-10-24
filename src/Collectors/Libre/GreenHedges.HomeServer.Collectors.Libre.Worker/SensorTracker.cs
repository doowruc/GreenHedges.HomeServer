using GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;
using GreenHedges.HomeServer.Collectors.Libre.Storage;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker;

public enum SensorObservation
{
    /// <summary>The connection reported no sensor.</summary>
    None,

    /// <summary>The first sensor this archive has seen.</summary>
    FirstSeen,

    Unchanged,

    /// <summary>A different sensor from the last one seen (a sensor swap).</summary>
    Changed,
}

/// <summary>
/// Records the connection's sensor in the archive on each poll, logs when it changes, says which sensor
/// a reading came from, and passes the sensor in use (with its warm-up) to <see cref="CollectorHealth"/>
/// so a new sensor's warm-up doesn't count as missing readings.
/// </summary>
/// <remarks>
/// Remembers the previous sensor too: the old sensor's last reading can arrive in the same poll that
/// first reports the new one (seen on a real swap), and it belongs to the old sensor.
/// </remarks>
public sealed class SensorTracker(
    GlucoseArchive archive,
    CollectorHealth health,
    IOptions<CollectorOptions> options,
    TimeProvider time,
    ILogger<SensorTracker> logger)
{
    private readonly CollectorOptions _options = options.Value;
    private KnownSensor? _current;
    private KnownSensor? _previous;
    private bool _loaded;

    /// <summary>Logs the most recently seen sensor and its expected end. For startup.</summary>
    public async Task LogCurrentAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        if (_current is null)
        {
            logger.LogInformation("No sensor recorded yet");
            return;
        }

        if (_current.ActivatedAt is { } activated)
        {
            var end = activated + _options.SensorLifetime;
            logger.LogInformation(
                "Current sensor {Serial}: activated {Activated:u}, expected end {End:u} ({Remaining} left)",
                _current.Serial,
                activated,
                end,
                FormatRemaining(end - time.GetUtcNow()));
        }
        else
        {
            logger.LogInformation("Current sensor {Serial}: activation time unknown", _current.Serial);
        }
    }

    public async Task<SensorObservation> ObserveAsync(SensorInfo? sensor, CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            await LoadAsync(cancellationToken);
        }

        if (sensor is null)
        {
            return SensorObservation.None;
        }

        // Kept in memory before the archive write, so readings are still attributed if that fails.
        var previous = _current;
        var observed = new KnownSensor(sensor.Serial, sensor.ActivatedAt, sensor.WarmupMinutes);
        if (previous is not null && previous.Serial != sensor.Serial)
        {
            _previous = previous;
        }

        _current = observed;
        health.RecordSensor(Status(observed));

        await archive.RecordSensorAsync(
            sensor.Serial,
            sensor.ActivatedAt,
            sensor.WarmupMinutes,
            sensor.ProductType,
            sensor.RawJson,
            time.GetUtcNow(),
            cancellationToken);

        if (sensor.Serial == previous?.Serial)
        {
            return SensorObservation.Unchanged;
        }

        var expectedEnd = sensor.ActivatedAt + _options.SensorLifetime;

        if (previous is null)
        {
            logger.LogInformation(
                "Sensor {Serial} in use: activated {Activated:u}, expected end {End:u}. Raw: {Raw}",
                sensor.Serial,
                sensor.ActivatedAt,
                expectedEnd,
                sensor.RawJson);
            return SensorObservation.FirstSeen;
        }

        logger.LogInformation(
            "Sensor changed: {Previous} -> {Serial}, activated {Activated:u}, warm-up {Warmup} min (readings expected from {ReadyAt:u}), expected end {End:u}. Raw: {Raw}",
            previous.Serial,
            sensor.Serial,
            sensor.ActivatedAt,
            Warmup(observed).TotalMinutes,
            Status(observed).ReadyAt,
            expectedEnd,
            sensor.RawJson);
        return SensorObservation.Changed;
    }

    /// <summary>
    /// The serial of the sensor a reading taken at <paramref name="readingTime"/> came from: the current
    /// sensor unless the reading is older than its activation, then the previous one (null if unknown).
    /// </summary>
    public string? SerialAt(DateTimeOffset readingTime) =>
        _current is { ActivatedAt: { } activated } && readingTime < activated
            ? _previous?.Serial
            : _current?.Serial;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var recent = await archive.GetRecentSensorsAsync(2, cancellationToken);
        _current = recent.Count > 0 ? Known(recent[0]) : null;
        _previous = recent.Count > 1 ? Known(recent[1]) : null;
        _loaded = true;

        if (_current is not null)
        {
            health.RecordSensor(Status(_current));
        }

        static KnownSensor Known(ArchivedSensor sensor) =>
            new(sensor.Serial, sensor.ActivatedAtUtc, sensor.WarmupMinutes);
    }

    private SensorStatus Status(KnownSensor sensor) =>
        new(
            sensor.Serial,
            sensor.ActivatedAt,
            sensor.ActivatedAt + Warmup(sensor),
            sensor.ActivatedAt + _options.SensorLifetime);

    private TimeSpan Warmup(KnownSensor sensor) =>
        sensor.WarmupMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : _options.SensorWarmup;

    private static string FormatRemaining(TimeSpan remaining) =>
        remaining <= TimeSpan.Zero
            ? "already past"
            : $"{(int)remaining.TotalDays}d {remaining.Hours}h";

    private sealed record KnownSensor(string Serial, DateTimeOffset? ActivatedAt, int? WarmupMinutes);
}
