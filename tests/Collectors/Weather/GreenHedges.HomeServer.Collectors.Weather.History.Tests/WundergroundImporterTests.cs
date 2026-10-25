using System.Net;
using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GreenHedges.HomeServer.Collectors.Weather.History.Tests;

public sealed class WundergroundImporterTests : IAsyncLifetime
{
    // 04:30 BST on Sunday 25 October 2026 (the clocks go back at 02:00 that morning, so it's 04:30 GMT): yesterday is the 24th.
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 4, 30, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"weather-wu-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeWunderground _api = new();
    private WeatherArchive _archive = null!;
    private WundergroundOptions _options = new() { StationId = "ISTATION1", ApiKey = "key", From = new DateOnly(2026, 10, 20), CallInterval = TimeSpan.Zero };

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _archive = new WeatherArchive(Path.Combine(_dir, "weather.db"));
        await _archive.InitialiseAsync(CancellationToken.None);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Fetches_every_day_from_the_start_to_yesterday_newest_first_and_stores_them()
    {
        _api.Day("20261024", FakeWunderground.Json((1792800294, 0.4), (1792800594, 1.2)));

        var result = await Importer().RunAsync(CancellationToken.None);

        Assert.Equal(["20261024", "20261023", "20261022", "20261021", "20261020"], _api.Dates);
        Assert.Equal((5, 2, 0, false), (result.DaysFetched, result.Observations, result.DaysRemaining, result.StoppedByRateLimit));
        var days = await _archive.GetWundergroundDaysAsync(CancellationToken.None);
        Assert.Equal(5, days.Count);
        Assert.All(days.Values, at => Assert.Equal(Now, at));
    }

    [Fact]
    public async Task Yesterday_is_fetched_again_the_next_night_then_left_alone()
    {
        await Importer().RunAsync(CancellationToken.None);
        _api.Dates.Clear();

        _time.Advance(TimeSpan.FromDays(1));
        await Importer().RunAsync(CancellationToken.None);
        Assert.Equal(["20261025", "20261024"], _api.Dates);

        _api.Dates.Clear();
        _time.Advance(TimeSpan.FromDays(1));
        await Importer().RunAsync(CancellationToken.None);
        Assert.Equal(["20261026", "20261025"], _api.Dates);
    }

    [Fact]
    public async Task Days_fetched_in_the_last_24_hours_count_against_the_daily_limit()
    {
        _options.MaxCallsPerDay = 3;
        var first = await Importer().RunAsync(CancellationToken.None);
        Assert.Equal((3, 2), (first.DaysFetched, first.DaysRemaining));

        // A restart an hour later: the budget is used up. Due: the two older days, and yesterday (still waiting for
        // its second fetch).
        _time.Advance(TimeSpan.FromHours(1));
        var second = await Importer().RunAsync(CancellationToken.None);
        Assert.Equal((0, 3), (second.DaysFetched, second.DaysRemaining));

        // The next night, 3 calls again: the new yesterday, the old one's second fetch, then the newest of the backlog.
        _api.Dates.Clear();
        _time.Advance(TimeSpan.FromDays(1));
        var third = await Importer().RunAsync(CancellationToken.None);
        Assert.Equal(["20261025", "20261024", "20261021"], _api.Dates);
        Assert.Equal(1, third.DaysRemaining);
    }

    [Fact]
    public async Task The_rate_limit_stops_the_run_keeping_what_was_fetched()
    {
        _api.Status("20261022", HttpStatusCode.TooManyRequests);

        var result = await Importer().RunAsync(CancellationToken.None);

        Assert.Equal((2, 3, true), (result.DaysFetched, result.DaysRemaining, result.StoppedByRateLimit));
        Assert.Equal(2, (await _archive.GetWundergroundDaysAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task A_failing_day_is_skipped_but_three_failures_in_a_row_fail_the_run()
    {
        _api.Status("20261023", HttpStatusCode.ServiceUnavailable);
        var result = await Importer().RunAsync(CancellationToken.None);
        Assert.Equal((4, 1), (result.DaysFetched, result.DaysRemaining));

        _api.Default = HttpStatusCode.BadGateway;
        _time.Advance(TimeSpan.FromDays(1));
        await Assert.ThrowsAsync<HttpRequestException>(() => Importer().RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_refused_key_fails_the_run()
    {
        _api.Default = HttpStatusCode.Unauthorized;

        await Assert.ThrowsAsync<WundergroundAuthException>(() => Importer().RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Today_so_far_is_stored_replacing_the_last_fetch_and_is_still_fetched_whole_after_it_ends()
    {
        _api.Day("20261025", FakeWunderground.Json((1792816494, 0.0)));
        Assert.Equal(1, await Importer().FetchTodayAsync(CancellationToken.None));

        _time.Advance(TimeSpan.FromHours(1));
        _api.Day("20261025", FakeWunderground.Json((1792816494, 0.0), (1792820094, 0.2)));
        Assert.Equal(2, await Importer().FetchTodayAsync(CancellationToken.None));

        var days = await _archive.ReadWundergroundDaysFetchedAfterAsync(Now, 10, CancellationToken.None);
        Assert.Equal(2, Assert.Single(days).Observations.Count);

        // Today isn't part of a run's backlog; the next night it's yesterday, fetched in full (and again the night after).
        Assert.DoesNotContain("20261025", (await RunDatesAsync()));
        _time.Advance(TimeSpan.FromDays(1));
        Assert.Contains("20261025", (await RunDatesAsync()));
    }

    [Fact]
    public async Task The_rate_limit_skips_todays_fetch_until_next_time()
    {
        _api.Status("20261025", HttpStatusCode.TooManyRequests);

        Assert.Null(await Importer().FetchTodayAsync(CancellationToken.None));
        Assert.Empty(await _archive.GetWundergroundDaysAsync(CancellationToken.None));
    }

    private async Task<List<string>> RunDatesAsync()
    {
        _api.Dates.Clear();
        await Importer().RunAsync(CancellationToken.None);
        return [.. _api.Dates];
    }

    [Fact]
    public void Local_days_end_at_local_midnight_across_the_clock_change()
    {
        var importer = Importer();

        Assert.Equal(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero), importer.EndUtc(new DateOnly(2026, 10, 24))); // BST
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero), importer.EndUtc(new DateOnly(2026, 10, 25))); // GMT
    }

    private WundergroundImporter Importer() => new(
        _archive,
        new WundergroundClient(new HttpClient(_api), Options.Create(_options)),
        Options.Create(_options),
        _time,
        NullLogger<WundergroundImporter>.Instance);
}
