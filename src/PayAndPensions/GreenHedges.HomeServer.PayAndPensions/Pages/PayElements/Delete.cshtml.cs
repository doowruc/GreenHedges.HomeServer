using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.PayElements;

/// <summary>Deletes a pay element, but not while it's on any payslips: those keep it, hidden if no longer used.</summary>
public class DeleteModel(AppDbContext db) : PersonPageModel(db)
{
    public int Id { get; private set; }

    public string ElementName { get; private set; } = string.Empty;

    public int Payslips { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var element = await Db.PayElements.Where(el => el.Id == id && el.PersonId == PersonId)
            .Select(el => new { el.Name, Payslips = el.Lines.Count }).FirstOrDefaultAsync();
        if (element is null) return NotFound();
        (Id, ElementName, Payslips) = (id, element.Name, element.Payslips);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var element = Db.PayElements.Where(el => el.Id == id && el.PersonId == PersonId);
        var name = await element.Select(el => el.Name).FirstOrDefaultAsync();
        if (name is null) return RedirectToIndex();
        if (await Db.PayslipLines.AnyAsync(l => l.PayElementId == id)) return RedirectToPage("Delete", new { personId = PersonId, id });

        await element.ExecuteDeleteAsync();
        TempData["Message"] = $"Deleted {name}.";
        return RedirectToIndex();
    }
}
