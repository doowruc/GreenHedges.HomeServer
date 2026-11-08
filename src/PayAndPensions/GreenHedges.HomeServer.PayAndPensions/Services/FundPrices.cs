using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>A fund's price on a date, in pence.</summary>
public sealed record SourcePrice(DateOnly Date, decimal Price);

/// <summary>Where fund prices come from.</summary>
public interface IFundPriceSource
{
    /// <summary>False until it has what it needs (an API key), when nothing should ask it for prices.</summary>
    bool IsConfigured { get; }

    /// <summary>The fund's prices from <paramref name="from"/> on, as many as it has.</summary>
    Task<IReadOnlyList<SourcePrice>> GetAsync(string isin, DateOnly from, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the funds' daily prices: each fund is asked for everything since its last stored price, so one call a day each
/// keeps it current and a missed day catches up by itself; a fund's first call fetches up to <see cref="HistoryDays"/>.
/// </summary>
public sealed class FundPrices(AppDbContext db, IFundPriceSource source, TimeProvider time, ILogger<FundPrices> logger)
{
    /// <summary>How far back a fund's first prices go: what the free tier of the feed offers.</summary>
    public const int HistoryDays = 365;

    public bool IsConfigured => source.IsConfigured;

    /// <summary>Updates every ISIN any user has set; one failing doesn't stop the others. Returns the prices added.</summary>
    public async Task<int> UpdateAllAsync(CancellationToken cancellationToken)
    {
        if (!source.IsConfigured) return 0;

        // Cross-user on purpose: this is background work, and prices are shared.
        var isins = await db.PensionFunds.IgnoreQueryFilters().Select(f => f.Isin).Distinct().ToListAsync(cancellationToken);
        var added = 0;
        foreach (var isin in isins)
        {
            try
            {
                added += await UpdateAsync(isin, cancellationToken);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or DbUpdateException
                && !cancellationToken.IsCancellationRequested)
            {
                // The type only: nothing that could carry the request's URL, which holds the API key.
                logger.LogWarning("Couldn't update a fund's prices: {Error}", e.GetType().Name);
            }
        }
        logger.LogInformation("Added {Added} fund prices for {Funds} funds", added, isins.Count);
        return added;
    }

    /// <summary>Fetches and stores one fund's new prices; failures are the caller's to handle. Returns the prices added.</summary>
    public async Task<int> UpdateAsync(string isin, CancellationToken cancellationToken)
    {
        if (!source.IsConfigured) return 0;

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var last = await db.FundPrices.Where(p => p.Isin == isin).MaxAsync(p => (DateOnly?)p.Date, cancellationToken);
        var from = last?.AddDays(1) ?? today.AddDays(-HistoryDays);
        if (from > today) return 0;

        var prices = (await source.GetAsync(isin, from, cancellationToken))
            .Where(p => p.Date >= from && p.Price > 0)
            .DistinctBy(p => p.Date)
            .Select(p => new FundPrice { Isin = isin, Date = p.Date, Price = p.Price })
            .ToList();
        db.FundPrices.AddRange(prices);
        await db.SaveChangesAsync(cancellationToken);
        return prices.Count;
    }
}
