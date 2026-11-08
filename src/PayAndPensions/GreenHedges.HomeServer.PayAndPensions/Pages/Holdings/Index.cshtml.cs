using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Holdings;

/// <summary>
/// A pension's units held in each fund, worked out from its purchases and sales (<see cref="Services.Holdings"/>), as at
/// today or ?asAt=, and their value at each fund's last dealing price.
/// </summary>
public class IndexModel(AppDbContext db) : PensionPageModel(db)
{
    /// <summary>Count deals up to and including this date; all of them when null.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? AsAt { get; set; }

    public List<FundHolding> Funds { get; private set; } = [];

    /// <summary>The funds' ISINs, by the fund's name.</summary>
    public Dictionary<string, string> Isins { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Purchases and sales without a fund (changed to one by hand), which aren't counted.</summary>
    public int WithoutFund { get; private set; }

    public bool HasTransactions { get; private set; }

    public decimal TotalCost => Funds.Sum(f => f.Cost);

    public decimal TotalValue => Funds.Sum(f => f.Value ?? 0);

    public bool AllValued => Funds.All(f => f.Value is not null);

    /// <summary>The total gain as a percentage of the total cost; null unless every fund has a value.</summary>
    public decimal? TotalGainPercent => AllValued ? Services.Holdings.GainPercent(TotalValue - TotalCost, TotalCost) : null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPensionAsync()) return NotFound();

        var transactions = Db.PensionTransactions.Where(tx => tx.PensionId == PensionId);
        HasTransactions = await transactions.AnyAsync();
        if (AsAt is { } asAt) transactions = transactions.Where(tx => tx.TradeDate <= asAt);
        var deals = transactions.Deals();

        WithoutFund = await deals.CountAsync(tx => tx.Fund == null);
        Isins = (await Db.PensionFunds.Where(f => f.PensionId == PensionId).Select(f => new { f.Fund, f.Isin }).ToListAsync())
            .ToDictionary(f => f.Fund, f => f.Isin, StringComparer.OrdinalIgnoreCase);
        Funds = Services.Holdings.Of(await deals.ToFundDealsAsync(), await DailyPricesAsync());
        return Page();
    }

    /// <summary>Each fund with an ISIN's latest price on or before the date shown. A few hundred rows a fund: picked here.</summary>
    private async Task<Dictionary<string, SourcePrice>> DailyPricesAsync()
    {
        var isins = Isins.Values.Distinct().ToList();
        var prices = await Db.FundPrices.Where(p => isins.Contains(p.Isin) && (AsAt == null || p.Date <= AsAt))
            .Select(p => new { p.Isin, p.Date, p.Price }).ToListAsync();
        var latest = prices.GroupBy(p => p.Isin).ToDictionary(g => g.Key, g => g.MaxBy(p => p.Date)!);
        return Isins.Where(f => latest.ContainsKey(f.Value))
            .ToDictionary(f => f.Key, f => new SourcePrice(latest[f.Value].Date, latest[f.Value].Price), StringComparer.OrdinalIgnoreCase);
    }
}
