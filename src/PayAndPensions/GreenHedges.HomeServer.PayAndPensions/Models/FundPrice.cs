using System.ComponentModel.DataAnnotations;

namespace GreenHedges.HomeServer.PayAndPensions.Models;

/// <summary>
/// A fund's published price on a date, from the daily feed (<see cref="Services.FundPrices"/>). Public market data shared
/// by anyone holding the fund, so it has no owner or audit columns.
/// </summary>
public class FundPrice
{
    [StringLength(12)]
    public string Isin { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    /// <summary>In pence, as the provider's own prices are.</summary>
    public decimal Price { get; set; }
}
