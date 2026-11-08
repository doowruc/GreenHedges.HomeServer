using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Holdings;

/// <summary>
/// Records a fund conversion, which the provider's exports leave out: on a date, all the units of one fund (as held
/// then, from the transactions) became so many of another's. Saved as two <see cref="TransactionKind.Conversion"/> lines,
/// one taking the old fund's units and cost away and one adding the new fund's units with that cost.
/// </summary>
public class ConvertModel(AppDbContext db, ILogger<ConvertModel> logger) : PensionPageModel(db)
{
    /// <summary>A conversion's two lines share this reference (and their date), which no export uses.</summary>
    public const string Reference = "CONVERSION";

    [BindProperty]
    public ConversionInput Conversion { get; set; } = new();

    /// <summary>The funds the pension has dealt in, for the lists.</summary>
    public List<string> Funds { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPensionAsync()) return NotFound();
        await LoadFundsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadPensionAsync()) return NotFound();
        await LoadFundsAsync();

        var c = Conversion;
        var to = c.ToFund?.Trim();
        if (to is not null) to = Funds.FirstOrDefault(f => f.Equals(to, StringComparison.OrdinalIgnoreCase)) ?? to;
        if (c.Date > DateOnly.FromDateTime(DateTime.Today)) ModelState.AddModelError("Conversion.Date", "The date can't be in the future.");
        if (c.Units is { } u && decimal.Round(u, 6) != u) ModelState.AddModelError("Conversion.Units", "Give the units to at most 6 decimal places.");
        if (to is not null && to.Equals(c.FromFund, StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError("Conversion.ToFund", "Choose a different fund from the one converted.");

        Services.FundHolding? from = null;
        if (ModelState.IsValid)
        {
            var date = c.Date!.Value;
            var deals = await Db.PensionTransactions.Where(tx => tx.PensionId == PensionId && tx.TradeDate <= date).Deals().ToFundDealsAsync();
            from = Services.Holdings.Of(deals).FirstOrDefault(h => h.Fund.Equals(c.FromFund, StringComparison.OrdinalIgnoreCase));
            if (from is not { Units: > 0 })
                ModelState.AddModelError("Conversion.FromFund", $"The pension held none of {c.FromFund} on {date:dd/MM/yyyy}.");
        }
        if (!ModelState.IsValid) return Page();

        PensionTransaction Line(string fund, decimal units, decimal cost, string description) => new()
        {
            PensionId = PensionId, TradeDate = c.Date!.Value, SettleDate = c.Date, Reference = Reference, Kind = TransactionKind.Conversion,
            Fund = fund, Quantity = units, Value = cost, Description = description,
        };
        Db.PensionTransactions.AddRange(
            Line(from!.Fund, -from.Units, from.Cost, $"Converted to {to}"),
            Line(to!, c.Units!.Value, -from.Cost, $"Converted from {from.Fund}"));
        await Db.SaveChangesAsync();
        logger.LogInformation("Recorded a fund conversion for pension {PensionId}", PensionId);

        TempData["Message"] = $"Recorded the conversion of {from.Units:#,##0.######} units of {from.Fund} to {c.Units:#,##0.######} units of {to}.";
        return RedirectToPage("Index", new { personId = PersonId, pensionId = PensionId });
    }

    private async Task LoadFundsAsync()
    {
        var funds = await Db.PensionTransactions.Where(tx => tx.PensionId == PensionId).Deals()
            .Where(tx => tx.Fund != null).Select(tx => tx.Fund!).Distinct().ToListAsync();
        Funds = funds.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
