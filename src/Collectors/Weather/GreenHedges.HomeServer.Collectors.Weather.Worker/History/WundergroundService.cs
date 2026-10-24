using GreenHedges.HomeServer.Collectors.Weather.History;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Weather.Worker.History;

/// <summary>
/// Keeps the archive's copy of Weather Underground's history up to date: a run a few minutes after startup (so a
/// first deploy starts the backfill at once), then every night at <see cref="WundergroundOptions.RunAt"/>. A failed
/// run is retried after <see cref="RetryAfter"/>. Off unless the station id and API key are set.
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
        logger.LogInformation("Weather Underground history from {From:yyyy-MM-dd}, nightly at {RunAt}", _options.From, _options.RunAt);

        var delay = StartupDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var result = await importer.RunAsync(stoppingToken);
                health.RecordWundergroundSuccess();
                delay = UntilNext(_options.RunAt, zone);
                if (result.StoppedByRateLimit)
                {
                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks, RetryAfter.Ticks));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                health.RecordWundergroundFailure(ex);
                logger.LogWarning(ex, "Weather Underground history: the run failed; retrying in {RetryAfter}", RetryAfter);
                delay = RetryAfter;
            }
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
