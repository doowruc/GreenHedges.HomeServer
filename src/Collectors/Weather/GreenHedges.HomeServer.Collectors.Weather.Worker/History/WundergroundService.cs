using GreenHedges.HomeServer.Collectors.Weather.History;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Weather.Worker.History;

/// <summary>
/// Keeps the archive's copy of Weather Underground's history up to date: a run a few minutes after startup (so a
/// first deploy starts the backfill at once), then every night at <see cref="WundergroundOptions.RunAt"/>; a failed
/// run is retried after <see cref="RetryAfter"/>. Today so far is fetched every <see cref="WundergroundOptions.TodayInterval"/>,
/// for live pressure. Off unless the station id and API key are set.
/// </summary>
public sealed class WundergroundService(
    IServiceProvider services,
    CollectorHealth health,
    IOptions<WundergroundOptions> options,
    TimeProvider time,
    ILogger<WundergroundService> logger) : BackgroundService
{
    internal static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    private readonly WundergroundOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Weather Underground history is off (Wunderground:StationId or Wunderground:ApiKey not set)");
            return;
        }

        var importer = services.GetRequiredService<WundergroundImporter>();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        logger.LogInformation(
            "Weather Underground history from {From:yyyy-MM-dd}, nightly at {RunAt}; today every {TodayInterval}",
            _options.From, _options.RunAt, _options.TodayInterval);

        var nextRun = time.GetUtcNow() + StartupDelay;
        DateTimeOffset? nextToday = _options.TodayInterval > TimeSpan.Zero ? nextRun : null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var wake = nextToday is { } t && t < nextRun ? t : nextRun;
            try
            {
                var delay = wake - time.GetUtcNow();
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, time, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                if (time.GetUtcNow() >= nextRun)
                {
                    nextRun = await RunAsync(importer, zone, stoppingToken);
                }

                if (nextToday is { } today && time.GetUtcNow() >= today)
                {
                    nextToday = time.GetUtcNow() + _options.TodayInterval;
                    if (await importer.FetchTodayAsync(stoppingToken) is not null)
                    {
                        health.RecordWundergroundSuccess();
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                health.RecordWundergroundFailure(ex);
                logger.LogWarning(ex, "Weather Underground: fetching today failed; trying again in {Interval}", _options.TodayInterval);
            }
        }
    }

    /// <summary>A full run (the backlog, yesterday's second fetch). Returns when the next one is due.</summary>
    private async Task<DateTimeOffset> RunAsync(WundergroundImporter importer, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        try
        {
            var result = await importer.RunAsync(cancellationToken);
            health.RecordWundergroundSuccess();
            var delay = UntilNext(_options.RunAt, zone);
            return time.GetUtcNow() + (result.StoppedByRateLimit ? TimeSpan.FromTicks(Math.Min(delay.Ticks, RetryAfter.Ticks)) : delay);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            health.RecordWundergroundFailure(ex);
            logger.LogWarning(ex, "Weather Underground history: the run failed; retrying in {RetryAfter}", RetryAfter);
            return time.GetUtcNow() + RetryAfter;
        }
    }

    /// <summary>How long until the next <paramref name="runAt"/> in <paramref name="zone"/>.</summary>
    internal TimeSpan UntilNext(TimeSpan runAt, TimeZoneInfo zone)
    {
        var now = time.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var next = local.Date + runAt;
        if (next <= local.DateTime)
        {
            next = next.AddDays(1);
        }

        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(next, DateTimeKind.Unspecified), zone);
        return nextUtc - now.UtcDateTime;
    }
}
