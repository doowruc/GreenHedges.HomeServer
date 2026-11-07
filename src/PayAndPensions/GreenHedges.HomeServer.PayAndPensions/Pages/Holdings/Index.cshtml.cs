using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
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

    /// <summary>Purchases and sales without a fund (changed to one by hand), which aren't counted.</summary>
    public int WithoutFund { get; private set; }

    public bool HasTransactions { get; private set; }

    public decimal TotalCost => Funds.Sum(f => f.Cost);

    public decimal TotalValue => Funds.Sum(f => f.Value ?? 0);

    public bool AllValued => Funds.All(f => f.Value is not null);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPensionAsync()) return NotFound();

        var transactions = Db.PensionTransactions.Where(tx => tx.PensionId == PensionId);
        HasTransactions = await transactions.AnyAsync();
        if (AsAt is { } asAt) transactions = transactions.Where(tx => tx.TradeDate <= asAt);
        var deals = transactions.Where(tx => (tx.Kind == TransactionKind.Purchase || tx.Kind == TransactionKind.Sale) && tx.Quantity != null);

        WithoutFund = await deals.CountAsync(tx => tx.Fund == null);
        Funds = Services.Holdings.Of(await deals.Where(tx => tx.Fund != null)
            .Select(tx => new FundDeal(tx.Fund!, tx.TradeDate, tx.Kind, tx.Quantity!.Value, tx.UnitPrice, tx.Value))
            .ToListAsync());
        return Page();
    }
}
