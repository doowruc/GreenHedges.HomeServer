using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Weather.Summaries;

/// <summary>
/// One interval of the station's history, from a service it uploaded to, in the dashboards' units (m/s, hPa, mm).
/// <see cref="RainMm"/> is the rain in this interval.
/// </summary>
public sealed record HistoryObservation(
    DateTime TimeUtc,
    int IntervalMinutes,
    double? TemperatureAvgC,
    double? TemperatureMinC,
    double? TemperatureMaxC,
    double? HumidityAvg,
    double? DewPointC,
    double? WindAvgMs,
    double? GustMaxMs,
    double? WindDirDeg,
    double? RainMm,
    double? PressureHpa,
    double? IndoorTemperatureC,
    double? IndoorHumidity);

/// <summary>
/// Publishes the archive's Weather Underground days to the store's <c>HistoryObservations</c>, each day whole (so a
/// day fetched again replaces its earlier copy), in the order they were fetched, tracking the last fetch time published.
/// </summary>
public sealed class HistoryPublisher(
    WeatherArchive archive,
    SummaryStore store,
    IOptions<SummaryOptions> options,
    TimeProvider time,
    ILogger<HistoryPublisher> logger)
{
    public const string WundergroundSource = "wunderground";

    /// <summary>Watermark name in <c>PublishState</c>: bump the suffix when the conversion changes, to republish every day.</summary>
    public const string WundergroundTarget = "wunderground-v1";

    private const double KmhToMs = 1 / 3.6;
    private const int DaysPerBatch = 50;

    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    /// <summary>Publishes every day fetched since the last run. Returns the number of days published.</summary>
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        var published = 0;
        while (true)
        {
            var after = DateTimeOffset.FromUnixTimeMilliseconds(await store.GetPublishedIdAsync(WundergroundTarget, cancellationToken));
            var days = await archive.ReadWundergroundDaysFetchedAfterAsync(after, DaysPerBatch, cancellationToken);
            foreach (var day in days)
            {
                var from = TimeZoneInfo.ConvertTimeToUtc(day.LocalDate.ToDateTime(TimeOnly.MinValue), _zone);
                var to = TimeZoneInfo.ConvertTimeToUtc(day.LocalDate.AddDays(1).ToDateTime(TimeOnly.MinValue), _zone);
                await store.ReplaceHistoryAsync(
                    WundergroundSource, from, to, FromWunderground(day.Observations), WundergroundTarget,
                    day.FetchedAtUtc.ToUnixTimeMilliseconds(), time.GetUtcNow(), cancellationToken);
                published++;
            }

            if (days.Count < DaysPerBatch)
            {
                if (published > 0)
                {
                    logger.LogDebug("Published {Days} Weather Underground days", published);
                }

                return published;
            }
        }
    }

    /// <summary>
    /// One local day's observations in the history's shape: km/h to m/s, pressure as the middle of its range, and rain
    /// per interval from the day's running total (which starts at 0 each local midnight).
    /// </summary>
    internal static IReadOnlyList<HistoryObservation> FromWunderground(IReadOnlyList<WundergroundObservation> observations)
    {
        var rows = new List<HistoryObservation>(observations.Count);
        double? dayTotal = 0;
        DateTimeOffset? previous = null;
        foreach (var o in observations)
        {
            double? rain = null;
            if (o.PrecipTotalMm is { } total)
            {
                // A lower total than the last one means the console reset it: count it in full.
                rain = Math.Round(dayTotal is { } before && total >= before ? total - before : total, 2);
                dayTotal = total;
            }

            var interval = previous is { } p ? (int)Math.Round((o.ObsTimeUtc - p).TotalMinutes) : 5;
            previous = o.ObsTimeUtc;
            rows.Add(new HistoryObservation(
                o.ObsTimeUtc.UtcDateTime,
                interval is > 0 and <= 60 ? interval : 5,
                o.TempAvgC, o.TempLowC, o.TempHighC, o.HumidityAvg, o.DewPointAvgC,
                o.WindAvgKmh * KmhToMs, o.GustHighKmh * KmhToMs, o.WindDirAvgDeg, rain,
                o is { PressureMaxHpa: { } max, PressureMinHpa: { } min } ? Math.Round((max + min) / 2, 1) : o.PressureMaxHpa ?? o.PressureMinHpa,
                null, null));
        }

        return rows;
    }
}
