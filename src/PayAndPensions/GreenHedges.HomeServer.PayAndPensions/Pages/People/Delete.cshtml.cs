using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.People;

/// <summary>
/// Deletes a person with their employers, pay elements and pensions. Not while they have payslips or pension statements or transactions: a history is never deleted
/// along with something else.
/// </summary>
public class DeleteModel(AppDbContext db) : PageModel
{
    public string PersonName { get; private set; } = string.Empty;

    public int Payslips { get; private set; }

    public int Statements { get; private set; }

    public int Transactions { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var person = await db.People.Where(p => p.Id == id)
            .Select(p => new { p.Name, Payslips = p.Payslips.Count, Statements = p.Pensions.Sum(pn => pn.StatementTypes.Sum(t => t.Statements.Count)),
                Transactions = p.Pensions.Sum(pn => pn.Transactions.Count) })
            .FirstOrDefaultAsync();
        if (person is null) return NotFound();
        (PersonName, Payslips, Statements, Transactions) = (person.Name, person.Payslips, person.Statements, person.Transactions);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var name = await db.People.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync();
        if (name is null) return RedirectToPage("Index");
        if (await db.Payslips.AnyAsync(s => s.PersonId == id) || await db.PensionStatements.AnyAsync(st => st.StatementType!.Pension!.PersonId == id)
            || await db.PensionTransactions.AnyAsync(tx => tx.Pension!.PersonId == id))
            return RedirectToPage("Delete", new { id });

        await db.People.Where(p => p.Id == id).ExecuteDeleteAsync();
        TempData["Message"] = $"Deleted {name}.";
        return RedirectToPage("Index");
    }
}
