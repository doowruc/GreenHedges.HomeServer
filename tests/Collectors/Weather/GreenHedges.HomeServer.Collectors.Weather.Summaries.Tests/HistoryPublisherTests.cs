using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Collectors.Weather.Summaries.Tests;

public sealed class HistoryPublisherTests : IAsyncLifetime
{
    // Saturday 24 October 2026, BST: local midnight is 23:00 UTC the day before.
    private static readonly DateOnly Day = new(2026, 10, 24);
    private static readonly DateTimeOffset DayStartUtc = new(2026, 10, 23, 23, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"weather-history-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 4, 30, 0, TimeSpan.Zero));
    private WeatherArchive _archive = null!;
    private TestSqlServer? _sql;
    private SummaryStore _store = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _archive = new WeatherArchive(Path.Combine(_dir, "weather.db"));
        await _archive.InitialiseAsync(CancellationToken.None);
        if (TestSqlServer.IsConfigured)
        {
            _sql = await TestSqlServer.CreateAsync();
            _store = new SummaryStore(_sql.ConnectionString);
            await _store.InitialiseAsync(CancellationToken.None);
        }
    }

    public async Task DisposeAsync()
    {
        if (_sql is not null)
        {
            await _sql.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Rain_per_interval_comes_from_the_days_running_total_and_units_are_converted()
    {
        var rows = HistoryPublisher.FromWunderground([Observation(5, 0.0), Observation(10, 0.4), Observation(15, 0.4), Observation(20, 1.2), Observation(25, null)]);

        Assert.Equal([0.0, 0.4, 0.0, 0.8, null], rows.Select(r => r.RainMm));
        var r = rows[1];
        Assert.Equal(DayStartUtc.AddMinutes(10).UtcDateTime, r.TimeUtc);
        Assert.Equal(5, r.IntervalMinutes);
        Assert.Equal((10.5, 10.0, 11.0, 90.0, 8.9), (r.TemperatureAvgC, r.TemperatureMinC, r.TemperatureMaxC, r.HumidityAvg, r.DewPointC));
        Assert.Equal(2.0, r.WindAvgMs!.Value, 6);   // 7.2 km/h
        Assert.Equal(4.0, r.GustMaxMs!.Value, 6);   // 14.4 km/h
        Assert.Equal((225.0, 1012.2), (r.WindDirDeg, r.PressureHpa));
        Assert.Null(r.IndoorTemperatureC);
    }

    [Fact]
    public void The_first_total_of_the_day_counts_from_midnight_and_a_drop_is_a_reset()
    {
        var rows = HistoryPublisher.FromWunderground([Observation(5, 0.8), Observation(10, 1.0), Observation(15, 0.2)]);

        Assert.Equal([0.8, 0.2, 0.2], rows.Select(r => r.RainMm));
    }

    [Fact]
    public void Weathercloud_rows_get_rain_per_interval_and_keep_the_indoor_readings()
    {
        var rows = HistoryPublisher.FromWeathercloud([Row(0, 0.0, 15), Row(10, 0.4, 15), Row(20, 0.4, 57), Row(30, 1.0, 15)]);

        Assert.Equal([0.0, 0.4, 0.0, 0.6], rows.Select(r => r.RainMm));
        Assert.Equal(10, rows[1].IntervalMinutes);
        Assert.Equal((22.0, 50.0, 1004.2), (rows[1].IndoorTemperatureC!.Value, rows[1].IndoorHumidity!.Value, rows[1].PressureHpa!.Value));
        Assert.Null(rows[2].TemperatureAvgC);          // 57 °C: a failing sensor, left out
        Assert.Equal(22.0, rows[2].IndoorTemperatureC); // the rest of the row stays
    }

    [Fact]
    public void Implausible_values_are_left_out_of_both_sources()
    {
        var o = HistoryPublisher.Plausible(new HistoryObservation(DateTime.UtcNow, 5, 54.7, -40, 12, 0, 10, 2, 3, 180, 0.4, 500, 22, 50));

        Assert.Equal((null, null, 12.0, null), (o.TemperatureAvgC, o.TemperatureMinC, o.TemperatureMaxC, o.HumidityAvg));
        Assert.Null(o.PressureHpa);
        Assert.Equal((2.0, 0.4, 22.0), (o.WindAvgMs!.Value, o.RainMm!.Value, o.IndoorTemperatureC!.Value));
    }

    [SqlServerFact]
    public async Task Weathercloud_days_are_published_by_version_and_a_reimport_replaces_a_day()
    {
        await _archive.SaveWeathercloudDaysAsync([new WeathercloudDay(Day, [Row(0, 0.0, 15), Row(10, 0.4, 15)])], "a.csv", _time.GetUtcNow(), CancellationToken.None);
        Assert.Equal(1, await Publisher().PublishPendingAsync(CancellationToken.None));

        await _archive.SaveWeathercloudDaysAsync([new WeathercloudDay(Day, [Row(0, 0.0, 15), Row(10, 0.4, 15), Row(20, 1.4, 15)])], "b.csv", _time.GetUtcNow(), CancellationToken.None);
        Assert.Equal(1, await Publisher().PublishPendingAsync(CancellationToken.None));
        Assert.Equal(0, await Publisher().PublishPendingAsync(CancellationToken.None));

        var rows = await _store.ReadHistoryAsync(HistoryPublisher.WeathercloudSource, DayStartUtc.UtcDateTime, DayStartUtc.AddDays(1).UtcDateTime, CancellationToken.None);
        Assert.Equal([0.0, 0.4, 1.0], rows.Select(r => r.RainMm));
        Assert.Equal(2, await _store.GetPublishedIdAsync(HistoryPublisher.WeathercloudTarget, CancellationToken.None));
    }

    private static WeathercloudRow Row(int minutes, double rainDay, double temperature) => new(
        DayStartUtc.AddMinutes(minutes), "", "", 22, temperature, 10, 50, 80, 3, 2, 180, 1004.2, rainDay, 0);

    [SqlServerFact]
    public async Task Each_day_is_published_whole_and_a_later_fetch_replaces_it()
    {
        await _archive.SaveWundergroundDayAsync(Day, _time.GetUtcNow(), "{}", [Observation(5, 0.0), Observation(10, 0.4)], CancellationToken.None);
        await _archive.SaveWundergroundDayAsync(Day.AddDays(-1), _time.GetUtcNow().AddSeconds(3), "", [], CancellationToken.None);

        Assert.Equal(2, await Publisher().PublishPendingAsync(CancellationToken.None));
        Assert.Equal(2, (await ReadDayAsync()).Count);
        Assert.Equal(0, await Publisher().PublishPendingAsync(CancellationToken.None));

        // Fetched again the next night, with late uploads: replaces the day, nothing duplicated.
        _time.Advance(TimeSpan.FromDays(1));
        await _archive.SaveWundergroundDayAsync(Day, _time.GetUtcNow(), "{}", [Observation(5, 0.0), Observation(10, 0.4), Observation(15, 1.0)], CancellationToken.None);
        Assert.Equal(1, await Publisher().PublishPendingAsync(CancellationToken.None));

        var rows = await ReadDayAsync();
        Assert.Equal([0.0, 0.4, 0.6], rows.Select(r => r.RainMm));
        Assert.Equal(_time.GetUtcNow().ToUnixTimeMilliseconds(), await _store.GetPublishedIdAsync(HistoryPublisher.WundergroundTarget, CancellationToken.None));
    }

    [SqlServerFact]
    public async Task Publishing_leaves_the_minute_summaries_and_other_days_alone()
    {
        await _store.WriteAsync([new MinuteSummary(DayStartUtc.UtcDateTime, 5, 10, 10, 10, 90, 1, 2, 90, 0, true, -1, 20)], "minutes-v1", 1, _time.GetUtcNow(), CancellationToken.None);
        await _archive.SaveWundergroundDayAsync(Day.AddDays(1), _time.GetUtcNow(), "{}", [Observation(24 * 60 + 5, 0.0)], CancellationToken.None);
        await _archive.SaveWundergroundDayAsync(Day, _time.GetUtcNow().AddSeconds(1), "{}", [Observation(5, 0.0)], CancellationToken.None);

        await Publisher().PublishPendingAsync(CancellationToken.None);

        Assert.Single(await _store.ReadMinutesAsync(DateTime.MinValue, DateTime.MaxValue, CancellationToken.None));
        Assert.Equal(2, (await _store.ReadHistoryAsync(HistoryPublisher.WundergroundSource, DateTime.MinValue, DateTime.MaxValue, CancellationToken.None)).Count);
    }

    private Task<IReadOnlyList<HistoryObservation>> ReadDayAsync() => _store.ReadHistoryAsync(
        HistoryPublisher.WundergroundSource, DayStartUtc.UtcDateTime, DayStartUtc.AddDays(1).UtcDateTime, CancellationToken.None);

    private HistoryPublisher Publisher() => new(
        _archive, _store, Options.Create(new SummaryOptions()), _time, NullLogger<HistoryPublisher>.Instance);

    private static WundergroundObservation Observation(int minutes, double? rain)
    {
        var time = DayStartUtc.AddMinutes(minutes);
        return new WundergroundObservation(
            time.ToUnixTimeSeconds(), time, 10.5, 11.0, 10.0, 90, 92, 88, 8.9, 7.2, 9.0, 14.4, 225, 1012.4, 1012.0, 0.0, rain, 1);
    }
}
