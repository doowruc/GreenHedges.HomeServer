using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Transactions;

/// <summary>
/// Changes an imported transaction's kind or fund; the rest is as the provider's export had it (one added by hand goes
/// to <see cref="ManualModel"/>, where all of it can be changed). A conversion (recorded on the
/// Holdings page or imported, as two lines) or a cost adjustment can't be changed, nor can another line become one.
/// </summary>
public class EditModel(AppDbContext db) : PensionPageModel(db)
{
    public sealed record Details(DateOnly TradeDate, string Reference, string Description, TransactionKind Kind, decimal? Quantity, decimal Value)
    {
        public bool IsConversion => Kind == TransactionKind.Conversion;

        public bool IsCostAdjustment => Kind == TransactionKind.CostAdjustment;
    }

    [BindProperty]
    public TransactionInput Transaction { get; set; } = new();

    public Details? Shown { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var transaction = await Find(id).Select(tx => new { tx.Kind, tx.Fund, tx.RowVersion, tx.Reference }).FirstOrDefaultAsync();
        if (transaction is null) return NotFound();
        if (IsManual(transaction.Reference, transaction.Kind)) return ToManual(id);
        Transaction = new TransactionInput { Kind = transaction.Kind, Fund = transaction.Fund, RowVersion = transaction.RowVersion };
        await LoadDetailsAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var existing = await Find(id).FirstOrDefaultAsync();
        if (existing is null) return NotFound();
        if (IsManual(existing.Reference, existing.Kind)) return ToManual(id);

        if (existing.Kind == TransactionKind.Conversion)
            ModelState.AddModelError(string.Empty, "A conversion can't be changed: delete it and record it again on the Holdings page.");
        else if (existing.Kind == TransactionKind.CostAdjustment)
            ModelState.AddModelError(string.Empty, "A cost adjustment can't be changed: it's as the provider's security movements have it.");
        else if (!Enum.IsDefined(Transaction.Kind) || Transaction.Kind is TransactionKind.Conversion or TransactionKind.CostAdjustment)
            ModelState.AddModelError("Transaction.Kind", "Choose a kind.");
        if (!ModelState.IsValid)
        {
            await LoadDetailsAsync(id);
            return Page();
        }

        Transaction.ApplyTo(existing);
        if (!await Db.TrySaveEditAsync(existing, Transaction.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this transaction after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Transaction.RowVersion = existing.RowVersion;
            ModelState.Remove("Transaction.RowVersion");
            await LoadDetailsAsync(id);
            return Page();
        }

        TempData["Message"] = $"Updated the transaction on {existing.TradeDate:d}.";
        return RedirectToTransactions(existing.TradeDate);
    }

    private static bool IsManual(string reference, TransactionKind kind) =>
        reference == ManualModel.Reference && kind is not (TransactionKind.Conversion or TransactionKind.CostAdjustment);

    private RedirectToPageResult ToManual(int id) => RedirectToPage("Manual", new { personId = PersonId, pensionId = PensionId, id });

    private IQueryable<PensionTransaction> Find(int id) =>
        Db.PensionTransactions.Where(tx => tx.Id == id && tx.PensionId == PensionId);

    private async Task LoadDetailsAsync(int id) =>
        Shown = await Find(id).Select(tx => new Details(tx.TradeDate, tx.Reference, tx.Description, tx.Kind, tx.Quantity, tx.Value)).FirstOrDefaultAsync();
}
