using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Transactions;

/// <summary>A pension's transactions in one tax year (the latest with any, or ?year=), latest first, with totals per kind.</summary>
public class IndexModel(AppDbContext db) : PensionPageModel(db)
{
    public sealed record Row(int Id, DateOnly TradeDate, string Reference, string Description, TransactionKind Kind, string? Fund,
        decimal? UnitPrice, decimal? Quantity, decimal Value);

    public sealed record KindTotal(TransactionKind Kind, int Count, decimal Value);

    /// <summary>The tax year shown, as the year it starts.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    /// <summary>The tax years with transactions, latest first (and the one shown, if it has none).</summary>
    public List<int> Years { get; private set; } = [];

    public int? PreviousYear => Years.Where(y => y < Year).Cast<int?>().FirstOrDefault();
    public int? NextYear => Years.Where(y => y > Year).Cast<int?>().LastOrDefault();
    public int? FirstYear => Years.Count > 0 && Years[^1] != Year ? Years[^1] : null;
    public int? LastYear => Years.Count > 0 && Years[0] != Year ? Years[0] : null;

    public List<Row> Rows { get; private set; } = [];

    /// <summary>The year's total of each kind it has, in the kinds' order.</summary>
    public List<KindTotal> Totals { get; private set; } = [];

    /// <summary>Money paid in: contributions and transfers in.</summary>
    public decimal PaidIn => Totals.Where(t => t.Kind is TransactionKind.Contribution or TransactionKind.TransferIn).Sum(t => t.Value);

    /// <summary>Every line together: the change in the pension's cash over the year.</summary>
    public decimal CashChange => Totals.Sum(t => t.Value);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPensionAsync()) return NotFound();

        // Few enough to work the tax years out in memory.
        var dates = await Db.PensionTransactions.Where(tx => tx.PensionId == PensionId).Select(tx => tx.TradeDate).Distinct().ToListAsync();
        Years = dates.Select(TaxYears.Of).Distinct().OrderDescending().ToList();
        Year ??= Years.Count > 0 ? Years[0] : TaxYears.Of(DateOnly.FromDateTime(DateTime.Today));
        if (!Years.Contains(Year.Value)) Years = Years.Append(Year.Value).OrderDescending().ToList();

        DateOnly from = TaxYears.Start(Year.Value), to = TaxYears.End(Year.Value);
        Rows = await Db.PensionTransactions
            .Where(tx => tx.PensionId == PensionId && tx.TradeDate >= from && tx.TradeDate <= to)
            .OrderByDescending(tx => tx.TradeDate).ThenByDescending(tx => tx.Id)
            .Select(tx => new Row(tx.Id, tx.TradeDate, tx.Reference, tx.Description, tx.Kind, tx.Fund, tx.UnitPrice, tx.Quantity, tx.Value))
            .ToListAsync();

        // Kinds are stored as text, so they're ordered here rather than in SQL.
        Totals = Rows.GroupBy(r => r.Kind).OrderBy(g => g.Key)
            .Select(g => new KindTotal(g.Key, g.Count(), g.Sum(r => r.Value))).ToList();
        return Page();
    }
}
