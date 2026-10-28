using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Employers;

/// <summary>Deletes an employer, but not while any payslips are from them.</summary>
public class DeleteModel(AppDbContext db) : PersonPageModel(db)
{
    public string EmployerName { get; private set; } = string.Empty;

    public int Payslips { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var employer = await Db.Employers.Where(m => m.Id == id && m.PersonId == PersonId)
            .Select(m => new { m.Name, Payslips = m.Payslips.Count }).FirstOrDefaultAsync();
        if (employer is null) return NotFound();
        (EmployerName, Payslips) = (employer.Name, employer.Payslips);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var employer = Db.Employers.Where(m => m.Id == id && m.PersonId == PersonId);
        var name = await employer.Select(m => m.Name).FirstOrDefaultAsync();
        if (name is null) return RedirectToIndex();
        if (await Db.Payslips.AnyAsync(s => s.EmployerId == id)) return RedirectToPage("Delete", new { personId = PersonId, id });

        await employer.ExecuteDeleteAsync();
        TempData["Message"] = $"Deleted {name}.";
        return RedirectToIndex();
    }
}
