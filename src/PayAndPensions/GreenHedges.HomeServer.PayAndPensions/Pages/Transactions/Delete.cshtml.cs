using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Transactions;

/// <summary>
/// Deletes one transaction (an import adds it back if a file still has it), or a conversion: both its lines, which
/// share its date and reference.
/// </summary>
public class DeleteModel(AppDbContext db) : PensionPageModel(db)
{
    public string Description { get; private set; } = string.Empty;

    public DateOnly TradeDate { get; private set; }

    public bool IsConversion { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var transaction = await Db.PensionTransactions.Where(tx => tx.Id == id && tx.PensionId == PensionId)
            .Select(tx => new { tx.TradeDate, tx.Description, tx.Value, tx.Kind }).FirstOrDefaultAsync();
        if (transaction is null) return NotFound();
        TradeDate = transaction.TradeDate;
        IsConversion = transaction.Kind == TransactionKind.Conversion;
        Description = IsConversion
            ? $"the fund conversion on {transaction.TradeDate:d} (both its lines)"
            : $"the transaction on {transaction.TradeDate:d}, {transaction.Description}, £{transaction.Value:N2}";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var transaction = await Db.PensionTransactions.Where(tx => tx.Id == id && tx.PensionId == PensionId)
            .Select(tx => new { tx.TradeDate, tx.Kind, tx.Reference }).FirstOrDefaultAsync();
        if (transaction is null) return RedirectToTransactions();

        if (transaction.Kind == TransactionKind.Conversion)
        {
            await Db.PensionTransactions.Where(tx => tx.PensionId == PensionId && tx.Kind == TransactionKind.Conversion
                && tx.TradeDate == transaction.TradeDate && tx.Reference == transaction.Reference).ExecuteDeleteAsync();
            TempData["Message"] = "Conversion deleted.";
        }
        else
        {
            await Db.PensionTransactions.Where(tx => tx.Id == id && tx.PensionId == PensionId).ExecuteDeleteAsync();
            TempData["Message"] = "Transaction deleted.";
        }
        return RedirectToTransactions(transaction.TradeDate);
    }
}
