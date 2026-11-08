namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>
/// Fetches the funds' new prices at startup and each morning at <see cref="RunAt"/>, UK time (a fund's price for a day is
/// published after that day). Only runs once the feed has a key.
/// </summary>
public sealed class FundPriceJob(IServiceScopeFactory scopes, TimeProvider time, ILogger<FundPriceJob> logger) : BackgroundService
{
    private static readonly TimeOnly RunAt = new(8, 0);

    private static readonly TimeZoneInfo UkTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<FundPrices>().UpdateAllAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning("Couldn't update the fund prices: {Error}", e.GetType().Name);
                }
                var now = time.GetUtcNow();
                await Task.Delay(NextRun(now) - now, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>The next <see cref="RunAt"/> after <paramref name="now"/>, UK time.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, UkTime);
        var day = DateOnly.FromDateTime(local.DateTime);
        if (TimeOnly.FromDateTime(local.DateTime) >= RunAt) day = day.AddDays(1);
        var next = day.ToDateTime(RunAt);
        return new DateTimeOffset(next, UkTime.GetUtcOffset(next));
    }
}
