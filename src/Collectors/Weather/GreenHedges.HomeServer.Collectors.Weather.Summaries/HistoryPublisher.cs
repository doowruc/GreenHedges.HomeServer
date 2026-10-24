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
/// Publishes the archive's Weather Underground and Weathercloud days to the store's <c>HistoryObservations</c>, each
/// local day whole (so a day fetched or imported again replaces its earlier copy), tracking how far each source has
/// got: Weather Underground by fetch time, Weathercloud by the day's import version. Values outside what the sensors
/// can plausibly read (e.g. a failing sensor's 57 °C) are left out; the archive keeps them as received.
/// </summary>
public sealed class HistoryPublisher(
    WeatherArchive archive,
    SummaryStore store,
    IOptions<SummaryOptions> options,
    TimeProvider time,
    ILogger<HistoryPublisher> logger)
{
    public const string WundergroundSource = "wunderground";

    public const string WeathercloudSource = "weathercloud";

    /// <summary>
    /// Watermark names in <c>PublishState</c>: bump the suffix when a conversion changes, to republish every day.
    /// Weather Underground v2 (2026-10-25): implausible values left out.
    /// </summary>
    public const string WundergroundTarget = "wunderground-v2";

    public const string WeathercloudTarget = "weathercloud-v1";

    private const double KmhToMs = 1 / 3.6;
    private const int DaysPerBatch = 50;

    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    /// <summary>Publishes every day fetched or imported since the last run. Returns the number of days published.</summary>
    public async Task<int> PublishPendingAsync(CancellationToken cancellationToken) =>
        await PublishWundergroundAsync(cancellationToken) + await PublishWeathercloudAsync(cancellationToken);

    private async Task<int> PublishWeathercloudAsync(CancellationToken cancellationToken)
    {
        var published = 0;
        while (true)
        {
            var after = await store.GetPublishedIdAsync(WeathercloudTarget, cancellationToken);
            var days = await archive.ReadWeathercloudDaysAfterAsync(after, DaysPerBatch, cancellationToken);
            foreach (var day in days)
            {
                var (from, to) = DayUtc(day.LocalDate);
                await store.ReplaceHistoryAsync(
                    WeathercloudSource, from, to, FromWeathercloud(day.Rows), WeathercloudTarget, day.Version, time.GetUtcNow(), cancellationToken);
                published++;
            }

            if (days.Count < DaysPerBatch)
            {
                return published;
            }
        }
    }

    private async Task<int> PublishWundergroundAsync(CancellationToken cancellationToken)
    {
        var published = 0;
        while (true)
        {
            var after = DateTimeOffset.FromUnixTimeMilliseconds(await store.GetPublishedIdAsync(WundergroundTarget, cancellationToken));
            var days = await archive.ReadWundergroundDaysFetchedAfterAsync(after, DaysPerBatch, cancellationToken);
            foreach (var day in days)
            {
                var (from, to) = DayUtc(day.LocalDate);
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
    /// One local day's Weathercloud rows in the history's shape: 10-minute intervals, and rain per interval from the
    /// day's running total (which starts at 0 each local midnight).
    /// </summary>
    internal static IReadOnlyList<HistoryObservation> FromWeathercloud(IReadOnlyList<WeathercloudRow> rows)
    {
        var result = new List<HistoryObservation>(rows.Count);
        double? dayTotal = 0;
        DateTimeOffset? previous = null;
        foreach (var w in rows)
        {
            double? rain = null;
            if (w.RainDayMm is { } total)
            {
                rain = Math.Round(dayTotal is { } before && total >= before ? total - before : total, 2);
                dayTotal = total;
            }

            var interval = previous is { } p ? (int)Math.Round((w.TimeUtc - p).TotalMinutes) : 10;
            previous = w.TimeUtc;
            result.Add(Plausible(new HistoryObservation(
                w.TimeUtc.UtcDateTime,
                interval is > 0 and <= 60 ? interval : 10,
                w.TemperatureC, null, null, w.Humidity, w.DewPointC, w.WindAvgMs, w.GustMs, w.WindDirDeg, rain, w.PressureHpa,
                w.IndoorTemperatureC, w.IndoorHumidity)));
        }

        return result;
    }

    /// <summary>The UTC span of a local day.</summary>
    private (DateTime From, DateTime To) DayUtc(DateOnly day) => (
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), _zone),
        TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), _zone));

    /// <summary>
    /// Leaves out values a working sensor can't read here (a failing sensor reported 54–57 °C in September 2024), so
    /// they don't distort the dashboards. Humidity of 0 is a missing reading.
    /// </summary>
    internal static HistoryObservation Plausible(HistoryObservation o) => o with
    {
        TemperatureAvgC = Within(o.TemperatureAvgC, -30, 45),
        TemperatureMinC = Within(o.TemperatureMinC, -30, 45),
        TemperatureMaxC = Within(o.TemperatureMaxC, -30, 45),
        DewPointC = Within(o.DewPointC, -40, 40),
        HumidityAvg = Within(o.HumidityAvg, 1, 100),
        WindAvgMs = Within(o.WindAvgMs, 0, 60),
        GustMaxMs = Within(o.GustMaxMs, 0, 80),
        WindDirDeg = Within(o.WindDirDeg, 0, 360),
        RainMm = Within(o.RainMm, 0, 100),
        PressureHpa = Within(o.PressureHpa, 900, 1090),
        IndoorTemperatureC = Within(o.IndoorTemperatureC, -10, 45),
        IndoorHumidity = Within(o.IndoorHumidity, 1, 100),
    };

    private static double? Within(double? value, double min, double max) => value is { } v && v >= min && v <= max ? v : null;

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
            rows.Add(Plausible(new HistoryObservation(
                o.ObsTimeUtc.UtcDateTime,
                interval is > 0 and <= 60 ? interval : 5,
                o.TempAvgC, o.TempLowC, o.TempHighC, o.HumidityAvg, o.DewPointAvgC,
                o.WindAvgKmh * KmhToMs, o.GustHighKmh * KmhToMs, o.WindDirAvgDeg, rain,
                o is { PressureMaxHpa: { } max, PressureMinHpa: { } min } ? Math.Round((max + min) / 2, 1) : o.PressureMaxHpa ?? o.PressureMinHpa,
                null, null)));
        }

        return rows;
    }
}
