using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Transactions;

/// <summary>
/// Adds a transaction by hand (no id), or changes one added by hand, for a pension whose provider has no export. Such
/// lines have the reference <see cref="Reference"/>, and unlike an imported line all of one can be changed. A purchase
/// or sale needs its fund and units, and its value or price: either is worked out from the other. A contribution sets
/// the pension received date on the payslips it adds up to, as an import does.
/// </summary>
public class ManualModel(AppDbContext db, PensionPayslips pensionPayslips) : PensionPageModel(db)
{
    /// <summary>The reference of a line entered by hand, which no export uses.</summary>
    public const string Reference = "MANUAL";

    /// <summary>The kinds that can be entered by hand: a conversion has its own page, and a cost adjustment only comes from a provider's file.</summary>
    public static readonly IReadOnlyList<TransactionKind> Kinds =
        Enum.GetValues<TransactionKind>().Where(k => k is not (TransactionKind.Conversion or TransactionKind.CostAdjustment)).ToList();

    /// <summary>The transaction to change; null to add one.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    /// <summary>Go back to the Holdings page afterwards, where it was opened from.</summary>
    [BindProperty(SupportsGet = true)]
    public bool FromHoldings { get; set; }

    [BindProperty]
    public ManualTransactionInput Transaction { get; set; } = new();

    /// <summary>The funds the pension has dealt in, to pick from.</summary>
    public List<string> Funds { get; private set; } = [];

    public static string Label(TransactionKind kind) =>
        typeof(TransactionKind).GetField(kind.ToString())!.GetCustomAttribute<DisplayAttribute>()?.Name ?? kind.ToString();

    public async Task<IActionResult> OnGetAsync(TransactionKind? kind, string? fund)
    {
        if (!await LoadPensionAsync()) return NotFound();
        await LoadFundsAsync();
        if (Id is null)
        {
            Transaction.Date = DateOnly.FromDateTime(DateTime.Today);
            if (kind is { } k && Kinds.Contains(k)) Transaction.Kind = k;
            Transaction.Fund = fund;
            return Page();
        }

        var existing = await Find().FirstOrDefaultAsync();
        if (existing is null) return NotFound();
        Transaction = new ManualTransactionInput
        {
            Date = existing.TradeDate, Kind = existing.Kind, Description = existing.Description, Fund = existing.Fund, Units = existing.Quantity,
            UnitPrice = existing.UnitPrice, Amount = ManualTransactionInput.AmountOf(existing.Kind, existing.Value), RowVersion = existing.RowVersion,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadPensionAsync()) return NotFound();
        await LoadFundsAsync();
        PensionTransaction? existing = null;
        if (Id is not null && (existing = await Find().FirstOrDefaultAsync()) is null) return NotFound();

        Validate();
        if (!ModelState.IsValid) return Page();

        var t = Transaction;
        var transaction = existing ?? new PensionTransaction { PensionId = PensionId, Reference = Reference };
        transaction.TradeDate = t.Date!.Value;
        transaction.SettleDate = t.Date;
        transaction.Kind = t.Kind;
        transaction.Fund = t.IsDeal ? t.Fund : null;
        transaction.Quantity = t.IsDeal ? t.Units : null;
        transaction.UnitPrice = t.IsDeal ? t.UnitPrice : null;
        transaction.Value = t.SignedValue();
        transaction.Description = !string.IsNullOrWhiteSpace(t.Description) ? t.Description.Trim()
            : t.IsDeal ? $"{t.Fund} {t.Units:0.######} @ {t.UnitPrice:0.######}" : Label(t.Kind);

        if (existing is null)
        {
            Db.PensionTransactions.Add(transaction);
            await Db.SaveChangesAsync();
        }
        else if (!await Db.TrySaveEditAsync(existing, t.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this transaction after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            t.RowVersion = existing.RowVersion;
            ModelState.Remove("Transaction.RowVersion");
            return Page();
        }

        var received = 0;
        if (transaction.Kind == TransactionKind.Contribution)
        {
            received = await pensionPayslips.SetReceivedAsync(await pensionPayslips.PlanAsync(PensionId));
            if (received > 0) await Db.SaveChangesAsync();
        }

        TempData["Message"] = $"{(existing is null ? "Added" : "Updated")} the transaction on {transaction.TradeDate:d}" +
            (received > 0 ? $", and set the pension received date on {received} {(received == 1 ? "payslip" : "payslips")}." : ".");
        return FromHoldings
            ? RedirectToPage("/Holdings/Index", new { personId = PersonId, pensionId = PensionId })
            : RedirectToTransactions(transaction.TradeDate);
    }

    /// <summary>Checks the input and fills in what's worked out: a deal's value from its price, or its price from its value.</summary>
    private void Validate()
    {
        var t = Transaction;
        if (!Kinds.Contains(t.Kind))
        {
            ModelState.AddModelError("Transaction.Kind", "Choose a kind.");
            return;
        }
        if (t.Date > DateOnly.FromDateTime(DateTime.Today)) ModelState.AddModelError("Transaction.Date", "The date can't be in the future.");

        if (!t.IsDeal)
        {
            if (t.Amount is null) ModelState.AddModelError("Transaction.Amount", "Give the value.");
            else if (t.Amount == 0) ModelState.AddModelError("Transaction.Amount", "The value can't be nothing.");
            else if (t.Amount < 0 && t.Kind != TransactionKind.Other)
                ModelState.AddModelError("Transaction.Amount", "Give the value without a minus sign: the kind says which way the money went.");
            return;
        }

        var fund = t.Fund?.Trim();
        if (string.IsNullOrEmpty(fund)) ModelState.AddModelError("Transaction.Fund", "Give the fund bought or sold.");
        else t.Fund = Funds.FirstOrDefault(f => f.Equals(fund, StringComparison.OrdinalIgnoreCase)) ?? fund;   // as the pension already spells it

        if (t.Units is null) ModelState.AddModelError("Transaction.Units", "Give the units bought or sold.");
        else if (decimal.Round(t.Units.Value, 6) != t.Units) ModelState.AddModelError("Transaction.Units", "Give the units to at most 6 decimal places.");

        if (t.Amount is null && t.UnitPrice is null) ModelState.AddModelError("Transaction.Amount", "Give the value, or the price to work it out from.");
        else if (t.Amount <= 0) ModelState.AddModelError("Transaction.Amount", "Give the value without a minus sign, and above nothing.");
        if (!ModelState.IsValid) return;

        t.Amount ??= Math.Round(t.Units!.Value * t.UnitPrice!.Value / 100, 2, MidpointRounding.AwayFromZero);
        t.UnitPrice ??= Math.Round(t.Amount.Value * 100 / t.Units!.Value, 6, MidpointRounding.AwayFromZero);
        if (t.Amount == 0) ModelState.AddModelError("Transaction.Amount", "The value comes to nothing: check the units and price.");
    }

    private IQueryable<PensionTransaction> Find() =>
        Db.PensionTransactions.Where(tx => tx.Id == Id && tx.PensionId == PensionId && tx.Reference == Reference
            && tx.Kind != TransactionKind.Conversion && tx.Kind != TransactionKind.CostAdjustment);

    private async Task LoadFundsAsync()
    {
        var funds = await Db.PensionTransactions.Where(tx => tx.PensionId == PensionId && tx.Fund != null).Select(tx => tx.Fund!).Distinct().ToListAsync();
        Funds = funds.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
