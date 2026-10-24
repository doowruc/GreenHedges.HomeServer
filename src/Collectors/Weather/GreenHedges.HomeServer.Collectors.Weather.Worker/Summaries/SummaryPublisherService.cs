using GreenHedges.HomeServer.Collectors.Weather.Summaries;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Weather.Worker.Summaries;

/// <summary>
/// Publishes minute summaries, and the station's history from Weather Underground, to SQL Server (for Grafana)
/// every <see cref="SummaryOptions.Interval"/>, starting with any backlog. If SQL Server is down it backs off and tries again; collection and the archive are unaffected,
/// and nothing is lost: the watermark only moves with a successful write. Off when
/// <c>ConnectionStrings:Weather</c> isn't set.
/// </summary>
public sealed class SummaryPublisherService(
    IServiceProvider services,
    IConfiguration configuration,
    CollectorHealth health,
    IOptions<SummaryOptions> options,
    TimeProvider time,
    ILogger<SummaryPublisherService> logger) : BackgroundService
{
    public const string ConnectionStringName = "Weather";

    private static readonly TimeSpan MinBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    private readonly SummaryOptions _options = options.Value;

    public static bool IsEnabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString(ConnectionStringName));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled(configuration))
        {
            logger.LogInformation("Summaries for Grafana are off (ConnectionStrings:Weather not set)");
            return;
        }

        // Resolved only when enabled: the store needs the connection string.
        var store = services.GetRequiredService<SummaryStore>();
        var publisher = services.GetRequiredService<SummaryPublisher>();
        var history = services.GetRequiredService<HistoryPublisher>();
        var initialised = false;
        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!initialised)
                {
                    await store.InitialiseAsync(stoppingToken);
                    initialised = true;
                    logger.LogInformation("Publishing minute summaries to SQL Server (schema v{Version})", SummaryStore.SchemaVersion);
                }

                var written = await publisher.PublishPendingAsync(stoppingToken);
                var days = await history.PublishPendingAsync(stoppingToken);
                health.RecordPublishSuccess();
                if (failures > 0 || written > 60)
                {
                    logger.LogInformation("Published {Minutes:N0} minute summaries", written);
                }

                if (days > 0)
                {
                    logger.LogInformation("Published {Days:N0} days of Weather Underground history", days);
                }

                failures = 0;
                await Task.Delay(_options.Interval, time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                health.RecordPublishFailure(ex);
                var backoff = TimeSpan.FromTicks(Math.Min(
                    MinBackoff.Ticks * (1L << Math.Min(failures - 1, 10)),
                    MaxBackoff.Ticks));
                logger.LogWarning(ex, "Publishing minute summaries failed ({Failures} in a row); retrying in {Backoff}", failures, backoff);
                try
                {
                    await Task.Delay(backoff, time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
