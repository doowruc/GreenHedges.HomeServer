using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using GreenHedges.HomeServer.Collectors.Weather.Storage;

namespace GreenHedges.HomeServer.Collectors.Weather.Summaries;

/// <summary>
/// One UTC minute of the station's readings, as the dashboards read them. Every value is derived from the
/// archive and can be rebuilt. A field is null when no message in the minute carried it (the sensor sends
/// temperature and rain in alternate messages, so a minute usually has both, but not always).
/// </summary>
/// <param name="MinuteUtc">The minute's start, UTC.</param>
/// <param name="Messages">Station messages in the minute, duplicates excluded (5 for a complete minute).</param>
/// <param name="WindDirDeg">The minute's wind direction: the vector mean of each message's direction, weighted by its average speed; null when calm.</param>
/// <param name="RainMm">Rain in the minute, from the change in the rain total; null when no message carried the total.</param>
/// <param name="BatteryOk">False if any message in the minute reported a low battery.</param>
public sealed record MinuteSummary(
    DateTime MinuteUtc,
    int Messages,
    double? TemperatureAvgC,
    double? TemperatureMinC,
    double? TemperatureMaxC,
    double? HumidityAvg,
    double? WindAvgMs,
    double? GustMaxMs,
    double? WindDirDeg,
    double? RainMm,
    bool? BatteryOk,
    double? RssiAvgDb,
    double? SnrAvgDb);

/// <summary>The rain total went down: the sensor restarted (a battery change), so counting starts again from 0.</summary>
public sealed record RainReset(long MessageId, DateTimeOffset ReceivedAtUtc, double FromMm, double ToMm);

/// <summary>Minute summaries for a run of readings, and the rain-total resets among them.</summary>
public sealed record SummaryResult(IReadOnlyList<MinuteSummary> Minutes, IReadOnlyList<RainReset> Resets);

/// <summary>Turns station readings into <see cref="MinuteSummary">minute summaries</see>. Pure: no I/O.</summary>
public static class MinuteSummariser
{
    /// <summary>A message identical to the previous one from the same device within this long is a repeat, and skipped.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Summarises <paramref name="readings"/> (in arrival order) by UTC minute. Rain per message is the change in
    /// the running total since the previous message that carried one; a total lower than the one before is a reset,
    /// and counts in full. A gap (collector down) puts all the rain since the last total in the first minute after it.
    /// </summary>
    /// <param name="previous">The reading just before <paramref name="readings"/>, to recognise a repeat of it.</param>
    /// <param name="previousRainTotalMm">The last rain total before <paramref name="readings"/>; without one, the first total is only a baseline (0 mm).</param>
    public static SummaryResult Summarise(
        IReadOnlyList<StationReading> readings, StationReading? previous, double? previousRainTotalMm)
    {
        var minutes = new List<MinuteSummary>();
        var resets = new List<RainReset>();
        var rainTotal = previousRainTotalMm;
        var last = previous;
        Accumulator? current = null;

        foreach (var reading in readings)
        {
            if (last is not null && IsRepeat(last, reading))
            {
                continue;
            }

            last = reading;
            var minute = MinuteOf(reading.ReceivedAtUtc);
            if (current is null || current.MinuteUtc != minute)
            {
                if (current is not null)
                {
                    minutes.Add(current.ToSummary());
                }

                current = new Accumulator(minute);
            }

            double? rain = null;
            if (reading.Weather.RainMm is { } total)
            {
                if (rainTotal is not { } before)
                {
                    rain = 0;
                }
                else if (total >= before)
                {
                    rain = total - before;
                }
                else
                {
                    rain = total;
                    resets.Add(new RainReset(reading.MessageId, reading.ReceivedAtUtc, before, total));
                }

                rainTotal = total;
            }

            current.Add(reading.Weather, rain);
        }

        if (current is not null)
        {
            minutes.Add(current.ToSummary());
        }

        return new SummaryResult(minutes, resets);
    }

    /// <summary>The start of the UTC minute that <paramref name="time"/> falls in.</summary>
    public static DateTime MinuteOf(DateTimeOffset time)
    {
        var utc = time.UtcDateTime;
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    }

    // The signal fields differ between repeats of one transmission; the readings don't.
    private static bool IsRepeat(StationReading earlier, StationReading later) =>
        earlier.DeviceId == later.DeviceId
        && later.ReceivedAtUtc - earlier.ReceivedAtUtc <= DuplicateWindow
        && earlier.Weather with { Rssi = null, Snr = null } == later.Weather with { Rssi = null, Snr = null };

    private sealed class Accumulator(DateTime minuteUtc)
    {
        private readonly List<double> _temperatures = [];
        private readonly List<double> _humidities = [];
        private readonly List<double> _windAverages = [];
        private readonly List<double> _rssis = [];
        private readonly List<double> _snrs = [];
        private double? _gustMax;
        private double _windX;
        private double _windY;
        private double? _rain;
        private bool? _batteryOk;
        private int _messages;

        public DateTime MinuteUtc { get; } = minuteUtc;

        public void Add(WeatherFields weather, double? rain)
        {
            _messages++;
            AddIfPresent(_temperatures, weather.TemperatureC);
            AddIfPresent(_humidities, weather.Humidity);
            AddIfPresent(_windAverages, weather.WindAvgMs);
            AddIfPresent(_rssis, weather.Rssi);
            AddIfPresent(_snrs, weather.Snr);

            if (weather.WindMaxMs is { } gust)
            {
                _gustMax = Math.Max(_gustMax ?? gust, gust);
            }

            if (weather is { WindAvgMs: { } speed, WindDirDeg: { } direction })
            {
                var radians = direction * Math.PI / 180;
                _windX += speed * Math.Sin(radians);
                _windY += speed * Math.Cos(radians);
            }

            if (rain is { } mm)
            {
                _rain = (_rain ?? 0) + mm;
            }

            if (weather.BatteryOk is { } ok)
            {
                _batteryOk = (_batteryOk ?? true) && ok != 0;
            }
        }

        public MinuteSummary ToSummary() => new(
            MinuteUtc,
            _messages,
            Average(_temperatures, 2),
            _temperatures.Count > 0 ? _temperatures.Min() : null,
            _temperatures.Count > 0 ? _temperatures.Max() : null,
            Average(_humidities, 1),
            Average(_windAverages, 2),
            _gustMax,
            WindDirection(),
            _rain is { } rain ? Math.Round(rain, 1) : null, // totals are in 0.1 mm; this removes float noise
            _batteryOk,
            Average(_rssis, 1),
            Average(_snrs, 1));

        private double? WindDirection()
        {
            // Calm (or no direction): no meaningful direction.
            if (Math.Abs(_windX) < 1e-9 && Math.Abs(_windY) < 1e-9)
            {
                return null;
            }

            var degrees = Math.Atan2(_windX, _windY) * 180 / Math.PI;
            return Math.Round((degrees + 360) % 360, 0) % 360;
        }

        private static void AddIfPresent(List<double> values, double? value)
        {
            if (value is { } v)
            {
                values.Add(v);
            }
        }

        private static double? Average(List<double> values, int decimals) =>
            values.Count > 0 ? Math.Round(values.Average(), decimals) : null;
    }
}
