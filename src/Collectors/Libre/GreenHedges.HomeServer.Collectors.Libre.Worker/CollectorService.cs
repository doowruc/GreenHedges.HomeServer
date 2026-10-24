using GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;
using GreenHedges.HomeServer.Collectors.Libre.Storage;
using GreenHedges.HomeServer.Collectors.Libre.Worker.Publishing;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker;

/// <summary>
/// Polls LibreLinkUp for the current reading every <see cref="CollectorOptions.CurrentInterval"/>,
/// and for graph history every <see cref="CollectorOptions.GraphInterval"/>, writing both to the archive.
/// A single loop, so there's only one place where errors and backoff are handled.
/// </summary>
public sealed class CollectorService(
    LibreLinkUpClient client,
    GlucoseArchive archive,
    CollectorHealth health,
    SensorTracker sensors,
    PublishSignal publishSignal,
    IOptions<CollectorOptions> options,
    TimeProvider time,
    ILogger<CollectorService> logger) : BackgroundService
{
    private readonly CollectorOptions _options = options.Value;
    private DateTimeOffset _nextGraphAt = DateTimeOffset.MinValue;
    private bool _warnedAboutMultipleConnections;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The archive is initialised (and migrated) in Program before any hosted service starts.
        logger.LogInformation(
            "Archive ready (schema v{SchemaVersion}): {Current} current and {Graph} graph readings",
            GlucoseArchive.SchemaVersion,
            await archive.CountAsync(ReadingSource.Current, stoppingToken),
            await archive.CountAsync(ReadingSource.Graph, stoppingToken));
        await sensors.LogCurrentAsync(stoppingToken);

        var consecutiveFailures = 0;
        var nextPollAt = time.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sensorTime = await CollectAsync(stoppingToken);
                health.RecordSuccess();
                consecutiveFailures = 0;

                var now = time.GetUtcNow();
                if (sensorTime is { } latest)
                {
                    nextPollAt = NextAlignedPoll(latest, now, _options.CurrentInterval, _options.PollOffset);
                }
                else
                {
                    // No reading to align to: keep a steady cadence from the previous deadline.
                    nextPollAt += _options.CurrentInterval;
                    if (nextPollAt <= now)
                    {
                        nextPollAt = now + _options.CurrentInterval;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (LibreLinkUpUserActionRequiredException ex)
            {
                health.RecordFailure(ex);
                logger.LogCritical(ex, "LibreLinkUp needs attention; pausing collection for {Pause}", _options.UserActionPause);
                nextPollAt = time.GetUtcNow() + _options.UserActionPause;
            }
            catch (RateLimitedException ex)
            {
                health.RecordFailure(ex);
                var pause = ex.RetryAfter is { } retryAfter && retryAfter > _options.MinRateLimitPause
                    ? retryAfter
                    : _options.MinRateLimitPause;
                logger.LogWarning(ex, "LibreLinkUp rate limit hit; pausing collection for {Pause}", pause);
                nextPollAt = time.GetUtcNow() + pause;
            }
            catch (Exception ex)
            {
                health.RecordFailure(ex);
                consecutiveFailures++;
                var backoff = Backoff(consecutiveFailures);
                logger.LogError(ex, "Collection failed ({Failures} in a row); retrying in {Backoff}", consecutiveFailures, backoff);
                nextPollAt = time.GetUtcNow() + backoff;
            }

            var delay = nextPollAt - time.GetUtcNow();
            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The next poll time, aligned to the sensor's reading cadence: <paramref name="offset"/> after a
    /// reading's sensor time, stepped by <paramref name="interval"/>, and at least half an interval from now.
    /// </summary>
    /// <remarks>
    /// Readings arrive a few seconds after their sensor time and stay "current" for about one interval.
    /// Polling at a fixed phase that happens to sit just after the sensor time races the upload and
    /// skips readings. Aligning about halfway through the window leaves slack in both directions, and
    /// follows the sensor's timing as it drifts.
    /// </remarks>
    internal static DateTimeOffset NextAlignedPoll(DateTimeOffset sensorTime, DateTimeOffset now, TimeSpan interval, TimeSpan offset)
    {
        var anchor = sensorTime + offset;
        var earliest = now + interval / 2;
        if (anchor >= earliest)
        {
            return anchor;
        }

        var steps = Math.Ceiling((earliest - anchor) / interval);
        return anchor + interval * steps;
    }

    /// <returns>Sensor time of the connection's current reading, if it had one.</returns>
    private async Task<DateTimeOffset?> CollectAsync(CancellationToken cancellationToken)
    {
        var connection = SelectConnection(await client.GetConnectionsAsync(cancellationToken));
        var receivedAt = time.GetUtcNow();

        try
        {
            await sensors.ObserveAsync(connection.Sensor, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sensor tracking is informational; never let it stop glucose collection.
            logger.LogWarning(ex, "Couldn't record sensor details");
        }

        if (connection.Current is { } current)
        {
            health.RecordReading(current.TimestampUtc);
            var inserted = await archive.InsertAsync(
                [ToArchived(current, ReadingSource.Current, receivedAt, sensors.SerialAt(current.TimestampUtc))],
                cancellationToken);
            if (inserted > 0)
            {
                publishSignal.Notify();
                logger.LogInformation(
                    "New reading {Timestamp:u}: {Value} mg/dL, trend {Trend}",
                    current.TimestampUtc,
                    current.ValueMgPerDl,
                    current.TrendArrow);
            }
            else
            {
                logger.LogDebug("No new reading since {Timestamp:u}", current.TimestampUtc);
            }
        }
        else
        {
            logger.LogWarning("Connection {PatientId} has no current measurement", connection.PatientId);
        }

        if (receivedAt >= _nextGraphAt)
        {
            await CollectGraphAsync(connection.PatientId, cancellationToken);
            _nextGraphAt = receivedAt + _options.GraphInterval;
        }

        return connection.Current?.TimestampUtc;
    }

    private async Task CollectGraphAsync(string patientId, CancellationToken cancellationToken)
    {
        var graph = await client.GetGraphAsync(patientId, cancellationToken);
        var receivedAt = time.GetUtcNow();

        var rows = graph.History.Select(reading => ToArchived(reading, ReadingSource.Graph, receivedAt)).ToList();
        if (graph.Current is { } current)
        {
            health.RecordReading(current.TimestampUtc);
            rows.Add(ToArchived(current, ReadingSource.Current, receivedAt, sensors.SerialAt(current.TimestampUtc)));
        }

        var inserted = await archive.InsertAsync(rows, cancellationToken);
        if (inserted > 0)
        {
            publishSignal.Notify();
        }

        logger.LogInformation(
            "Graph backfill: {Inserted} new of {Total} readings ({From:u} to {To:u})",
            inserted,
            rows.Count,
            graph.History.Count > 0 ? graph.History.Min(r => r.TimestampUtc) : null,
            graph.History.Count > 0 ? graph.History.Max(r => r.TimestampUtc) : null);
    }

    private Connection SelectConnection(IReadOnlyList<Connection> connections)
    {
        if (connections.Count == 0)
        {
            throw new NoConnectionsException();
        }

        if (!string.IsNullOrEmpty(_options.PatientId))
        {
            return connections.FirstOrDefault(c => c.PatientId == _options.PatientId)
                ?? throw new InvalidOperationException(
                    $"Configured Collector:PatientId {_options.PatientId} is not among the account's connections " +
                    $"({string.Join(", ", connections.Select(c => c.PatientId))}).");
        }

        if (connections.Count > 1 && !_warnedAboutMultipleConnections)
        {
            logger.LogWarning(
                "The follower account has {Count} connections; collecting the first ({PatientId}). Set Collector:PatientId to choose. Connections: {PatientIds}",
                connections.Count,
                connections[0].PatientId,
                connections.Select(c => c.PatientId));
            _warnedAboutMultipleConnections = true;
        }

        return connections[0];
    }

    private TimeSpan Backoff(int consecutiveFailures)
    {
        var factor = Math.Pow(2, Math.Min(consecutiveFailures - 1, 16));
        var backoff = _options.FailureBackoff * factor;
        return backoff < _options.MaxFailureBackoff ? backoff : _options.MaxFailureBackoff;
    }

    // Graph history can span sensors, so only current readings carry the sensor serial.
    private static ArchivedReading ToArchived(
        GlucoseReading reading,
        ReadingSource source,
        DateTimeOffset receivedAt,
        string? sensorSerial = null) =>
        new(
            reading.TimestampUtc,
            source,
            reading.TimestampLocal,
            reading.ValueMgPerDl,
            reading.TrendArrow,
            reading.MeasurementColor,
            reading.IsHigh,
            reading.IsLow,
            receivedAt,
            reading.RawJson,
            sensorSerial);
}
