using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Pensions;

/// <summary>Deletes a pension, but not while payslips go to it.</summary>
public class DeleteModel(AppDbContext db) : PersonPageModel(db)
{
    public string PensionName { get; private set; } = string.Empty;

    public int Payslips { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var pension = await Db.Pensions.Where(pn => pn.Id == id && pn.PersonId == PersonId)
            .Select(pn => new { pn.Name, Payslips = pn.Payslips.Count }).FirstOrDefaultAsync();
        if (pension is null) return NotFound();
        (PensionName, Payslips) = (pension.Name, pension.Payslips);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var pension = Db.Pensions.Where(pn => pn.Id == id && pn.PersonId == PersonId);
        var name = await pension.Select(pn => pn.Name).FirstOrDefaultAsync();
        if (name is null) return RedirectToIndex();
        if (await Db.Payslips.AnyAsync(s => s.PensionId == id)) return RedirectToPage("Delete", new { personId = PersonId, id });

        await pension.ExecuteDeleteAsync();
        TempData["Message"] = $"Deleted {name}.";
        return RedirectToIndex();
    }
}
