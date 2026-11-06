using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Transactions;

/// <summary>Deletes one transaction (an import adds it back if a file still has it).</summary>
public class DeleteModel(AppDbContext db) : PensionPageModel(db)
{
    public string Description { get; private set; } = string.Empty;

    public DateOnly TradeDate { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var transaction = await Db.PensionTransactions.Where(tx => tx.Id == id && tx.PensionId == PensionId)
            .Select(tx => new { tx.TradeDate, tx.Description, tx.Value }).FirstOrDefaultAsync();
        if (transaction is null) return NotFound();
        TradeDate = transaction.TradeDate;
        Description = $"the transaction on {transaction.TradeDate:d}, {transaction.Description}, £{transaction.Value:N2}";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPensionAsync()) return NotFound();
        var transaction = Db.PensionTransactions.Where(tx => tx.Id == id && tx.PensionId == PensionId);
        var date = await transaction.Select(tx => (DateOnly?)tx.TradeDate).FirstOrDefaultAsync();
        await transaction.ExecuteDeleteAsync();
        if (date is not null) TempData["Message"] = "Transaction deleted.";
        return RedirectToTransactions(date);
    }
}
