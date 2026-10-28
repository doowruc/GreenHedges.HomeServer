using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.People;

/// <summary>
/// Deletes a person with their employers and pay elements. Not while they have payslips: a pay history is never deleted
/// along with something else.
/// </summary>
public class DeleteModel(AppDbContext db) : PageModel
{
    public string PersonName { get; private set; } = string.Empty;

    public int Payslips { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var person = await db.People.Where(p => p.Id == id)
            .Select(p => new { p.Name, Payslips = p.Payslips.Count }).FirstOrDefaultAsync();
        if (person is null) return NotFound();
        (PersonName, Payslips) = (person.Name, person.Payslips);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var name = await db.People.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync();
        if (name is null) return RedirectToPage("Index");
        if (await db.Payslips.AnyAsync(s => s.PersonId == id)) return RedirectToPage("Delete", new { id });

        await db.People.Where(p => p.Id == id).ExecuteDeleteAsync();
        TempData["Message"] = $"Deleted {name}.";
        return RedirectToPage("Index");
    }
}
