using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Weather.Summaries;

/// <summary>Where and how often the minute summaries are published.</summary>
public sealed class SummaryOptions
{
    public const string SectionName = "Summaries";

    /// <summary>How often new readings are summarised and published.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Readings per batch (one transaction each); about 16 hours of them.</summary>
    public int BatchSize { get; set; } = 5000;

    /// <summary>The station's time zone, for the local days of its uploaded history.</summary>
    public string TimeZone { get; set; } = "Europe/London";
}

/// <summary>
/// Publishes minute summaries of the archive's station readings to the <see cref="SummaryStore"/>, in archive id
/// order, tracking progress with a watermark kept in the store, so an empty store is refilled from the start. Each
/// batch re-summarises whole minutes from the first new reading's minute on, so a minute that was still filling at
/// the last run is completed. Safe to re-run at any point: the watermark moves in the same transaction as the minutes.
/// </summary>
public sealed class SummaryPublisher(
    WeatherArchive archive,
    SummaryStore store,
    IOptions<SummaryOptions> options,
    TimeProvider time,
    ILogger<SummaryPublisher> logger)
{
    /// <summary>
    /// Watermark name in the store's <c>PublishState</c>. Bump the suffix when the summaries' calculation changes: the
    /// new name starts from zero, so every minute is recalculated once (and overwritten in place).
    /// </summary>
    public const string Target = "minutes-v1";

    private readonly SummaryOptions _options = options.Value;

    /// <summary>Publishes everything not yet published. Returns the number of minutes written.</summary>
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        var written = 0;
        while (true)
        {
            var afterId = await store.GetPublishedIdAsync(Target, cancellationToken);
            var batch = await archive.ReadStationReadingsAfterAsync(afterId, _options.BatchSize, cancellationToken);
            if (batch.Count == 0)
            {
                return written;
            }

            var lastId = batch[^1].MessageId;
            var from = new DateTimeOffset(MinuteSummariser.MinuteOf(batch[0].ReceivedAtUtc));
            var readings = await archive.ReadStationReadingsFromAsync(from, lastId, cancellationToken);
            var previous = await archive.GetLatestStationReadingBeforeAsync(from, withRain: false, cancellationToken);
            var previousRain = previous?.Weather.RainMm is not null
                ? previous
                : await archive.GetLatestStationReadingBeforeAsync(from, withRain: true, cancellationToken);

            var result = MinuteSummariser.Summarise(readings, previous, previousRain?.Weather.RainMm);
            await store.WriteAsync(result.Minutes, Target, lastId, time.GetUtcNow(), cancellationToken);
            written += result.Minutes.Count;

            foreach (var reset in result.Resets.Where(r => r.MessageId > afterId))
            {
                logger.LogWarning(
                    "Rain total went down at {Time:u}, from {From} to {To} mm: the sensor restarted (battery change?), counting from 0",
                    reset.ReceivedAtUtc,
                    reset.FromMm,
                    reset.ToMm);
            }

            logger.LogDebug(
                "Summarised readings {First}–{Last} into {Minutes} minutes from {From:u}",
                batch[0].MessageId,
                lastId,
                result.Minutes.Count,
                from);

            if (batch.Count < _options.BatchSize)
            {
                return written;
            }
        }
    }
}
