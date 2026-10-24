using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Weather.History;

/// <summary>What one import run did.</summary>
/// <param name="DaysRemaining">Days still due after the run (left for the next one: the daily limit, or a stop).</param>
/// <param name="StoppedByRateLimit">Weather Underground answered 429, so the run stopped early.</param>
public sealed record WundergroundRunResult(int DaysFetched, int Observations, int DaysRemaining, bool StoppedByRateLimit);

/// <summary>
/// Brings the archive's copy of Weather Underground's history up to date: every local day from
/// <see cref="WundergroundOptions.From"/> to yesterday that hasn't been fetched in full yet (never fetched, or fetched
/// before <see cref="WundergroundOptions.CompleteAfter"/> had passed since it ended), newest first, within
/// <see cref="WundergroundOptions.MaxCallsPerDay"/> counting the days already fetched in the last 24 hours. Safe to run
/// at any time and to re-run: each day is stored whole, replacing any earlier copy.
/// </summary>
public sealed class WundergroundImporter(
    WeatherArchive archive,
    WundergroundClient client,
    IOptions<WundergroundOptions> options,
    TimeProvider time,
    ILogger<WundergroundImporter> logger)
{
    /// <summary>Consecutive failed calls (other than auth or rate limit) after which a run gives up.</summary>
    private const int MaxConsecutiveFailures = 3;

    private readonly WundergroundOptions _options = options.Value;
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public async Task<WundergroundRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var fetched = await archive.GetWundergroundDaysAsync(cancellationToken);
        var due = DueDays(fetched, now);
        var budget = Math.Max(0, _options.MaxCallsPerDay - fetched.Values.Count(f => now - f < TimeSpan.FromDays(1)));

        if (due.Count == 0)
        {
            return new WundergroundRunResult(0, 0, 0, false);
        }

        logger.LogInformation(
            "Weather Underground: {Due:N0} days to fetch ({From:yyyy-MM-dd} to {To:yyyy-MM-dd}), {Budget:N0} calls available today",
            due.Count, due[^1], due[0], budget);

        var days = 0;
        var observations = 0;
        var failures = 0;
        foreach (var day in due.Take(budget))
        {
            if (days + failures > 0)
            {
                await Task.Delay(_options.CallInterval, time, cancellationToken);
            }

            WundergroundResponse response;
            try
            {
                response = await client.FetchDayAsync(day, cancellationToken);
            }
            catch (WundergroundRateLimitException)
            {
                logger.LogWarning("Weather Underground's rate limit was reached after {Days} days; the rest wait for the next run", days);
                return new WundergroundRunResult(days, observations, due.Count - days, true);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                failures++;
                logger.LogWarning(ex, "Weather Underground: fetching {Day:yyyy-MM-dd} failed ({Failures} in a row)", day, failures);
                if (failures >= MaxConsecutiveFailures)
                {
                    throw;
                }

                continue;
            }

            failures = 0;
            await archive.SaveWundergroundDayAsync(day, time.GetUtcNow(), response.RawJson, response.Observations, cancellationToken);
            days++;
            observations += response.Observations.Count;
            if (days % 100 == 0)
            {
                logger.LogInformation("Weather Underground: {Days:N0} of {Due:N0} days fetched", days, due.Count);
            }
        }

        logger.LogInformation(
            "Weather Underground: fetched {Days:N0} days ({Observations:N0} observations); {Remaining:N0} still to fetch",
            days, observations, due.Count - days);
        return new WundergroundRunResult(days, observations, due.Count - days, false);
    }

    /// <summary>The days to fetch, newest first: yesterday's and recent gaps come before the backlog.</summary>
    internal List<DateOnly> DueDays(IReadOnlyDictionary<DateOnly, DateTimeOffset> fetched, DateTimeOffset now)
    {
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _zone).DateTime).AddDays(-1);
        var due = new List<DateOnly>();
        for (var day = yesterday; day >= _options.From; day = day.AddDays(-1))
        {
            if (!fetched.TryGetValue(day, out var at) || at < EndUtc(day) + _options.CompleteAfter)
            {
                due.Add(day);
            }
        }

        return due;
    }

    /// <summary>When a local day ends, in UTC.</summary>
    internal DateTimeOffset EndUtc(DateOnly day) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), _zone), TimeSpan.Zero);
}
