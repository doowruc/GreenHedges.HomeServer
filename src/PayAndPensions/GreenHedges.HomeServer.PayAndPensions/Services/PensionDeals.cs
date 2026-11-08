using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>A pension's purchases, sales, conversions and cost adjustments, for <see cref="Holdings"/>, and the funds' daily prices.</summary>
public static class PensionDeals
{
    /// <summary>The transactions that change units or cost: purchases, sales and conversions with a quantity (with or without a fund), and cost adjustments.</summary>
    public static IQueryable<PensionTransaction> Deals(this IQueryable<PensionTransaction> transactions) =>
        transactions.Where(tx => tx.Kind == TransactionKind.CostAdjustment || ((tx.Kind == TransactionKind.Purchase || tx.Kind == TransactionKind.Sale
            || tx.Kind == TransactionKind.Conversion) && tx.Quantity != null));

    /// <summary>The deals with a fund.</summary>
    public static Task<List<FundDeal>> ToFundDealsAsync(this IQueryable<PensionTransaction> deals) =>
        deals.Where(tx => tx.Fund != null)
            .Select(tx => new FundDeal(tx.Fund!, tx.TradeDate, tx.Kind, tx.Quantity ?? 0, tx.UnitPrice, tx.Value))
            .ToListAsync();

    /// <summary>Each ISIN's latest price, on or before <paramref name="asAt"/> when given. A few hundred rows an ISIN: picked here.</summary>
    public static async Task<Dictionary<string, SourcePrice>> LatestPricesAsync(this AppDbContext db, IReadOnlyCollection<string> isins, DateOnly? asAt = null)
    {
        var prices = await db.FundPrices.Where(p => isins.Contains(p.Isin) && (asAt == null || p.Date <= asAt))
            .Select(p => new { p.Isin, p.Date, p.Price }).ToListAsync();
        return prices.GroupBy(p => p.Isin).Select(g => g.MaxBy(p => p.Date)!).ToDictionary(p => p.Isin, p => new SourcePrice(p.Date, p.Price));
    }
}
