using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Holdings;

/// <summary>A pension's purchases, sales, conversions and cost adjustments, for <see cref="Services.Holdings"/>.</summary>
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
}
