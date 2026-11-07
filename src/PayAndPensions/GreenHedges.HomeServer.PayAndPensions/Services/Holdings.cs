using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>A purchase, sale or conversion of units in a fund: a transaction with a fund and a quantity.</summary>
/// <param name="Quantity">Positive on a purchase or sale; on a conversion, negative for the units converted away.</param>
/// <param name="UnitPrice">In pence, as the exports give it.</param>
/// <param name="Value">
/// In pounds: negative for a purchase (money out), positive for a sale; on a conversion, the cost moved (positive on
/// the units converted away, negative on those received).
/// </param>
public sealed record FundDeal(string Fund, DateOnly TradeDate, TransactionKind Kind, decimal Quantity, decimal? UnitPrice, decimal Value);

/// <summary>
/// A fund's units held, from its purchases and sales: what they cost (purchases less sales), and their value at the
/// last price the fund was dealt at (<paramref name="Price"/> in pence, on <paramref name="PriceDate"/>): null without one.
/// </summary>
/// <param name="Converted">Its units were all converted to another fund.</param>
public sealed record FundHolding(string Fund, int Deals, decimal Units, decimal Cost, decimal? Price, DateOnly? PriceDate, decimal? Value,
    bool Converted = false)
{
    /// <summary>Every unit bought has been sold (or converted).</summary>
    public bool SoldOut => Units == 0;

    /// <summary>Value less cost; null without a value.</summary>
    public decimal? Gain => Value - Cost;
}

/// <summary>
/// Each fund's units held, worked out from a pension's purchases and sales: a purchase adds its units, a sale takes
/// them away (the exports give both as positive quantities), and a conversion moves units and cost from one fund to
/// another (<see cref="TransactionKind.Conversion"/>). The value is the units at the latest deal's price: an export has
/// no prices but its deals, so it's only as recent as the last purchase or sale. Pure, so unit tested.
/// </summary>
public static class Holdings
{
    /// <summary>The funds dealt in, largest value first (then by name), those sold out last.</summary>
    public static List<FundHolding> Of(IEnumerable<FundDeal> deals) =>
        deals.Where(d => d.Kind is TransactionKind.Purchase or TransactionKind.Sale or TransactionKind.Conversion && d.Quantity != 0)
            .GroupBy(d => d.Fund, StringComparer.OrdinalIgnoreCase)
            .Select(Holding)
            .OrderBy(h => h.SoldOut).ThenByDescending(h => h.Value ?? 0).ThenBy(h => h.Fund, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static FundHolding Holding(IGrouping<string, FundDeal> fund)
    {
        var units = fund.Sum(d => d.Kind switch
        {
            TransactionKind.Purchase => Math.Abs(d.Quantity),
            TransactionKind.Sale => -Math.Abs(d.Quantity),
            _ => d.Quantity,
        });
        var cost = -fund.Sum(d => d.Value);
        var priced = fund.Where(d => d.UnitPrice is > 0).OrderBy(d => d.TradeDate).LastOrDefault();
        var value = priced is null ? (decimal?)null : Math.Round(units * priced.UnitPrice!.Value / 100, 2, MidpointRounding.AwayFromZero);
        var converted = units == 0 && fund.MaxBy(d => d.TradeDate) is { Kind: TransactionKind.Conversion, Quantity: < 0 };
        return new FundHolding(fund.Key, fund.Count(), units, cost, priced?.UnitPrice, priced?.TradeDate, value, converted);
    }
}
