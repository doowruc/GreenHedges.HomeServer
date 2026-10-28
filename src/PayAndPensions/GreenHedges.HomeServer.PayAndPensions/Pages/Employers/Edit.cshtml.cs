using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Employers;

public class EditModel(AppDbContext db) : PersonPageModel(db)
{
    [BindProperty]
    public EmployerInput Employer { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var employer = await Db.Employers.Where(m => m.Id == id && m.PersonId == PersonId)
            .Select(EmployerInput.Projection).FirstOrDefaultAsync();
        if (employer is null) return NotFound();
        Employer = employer;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var existing = await Db.Employers.FirstOrDefaultAsync(m => m.Id == id && m.PersonId == PersonId);
        if (existing is null) return NotFound();

        await EmployerRules.ValidateAsync(Db, PersonId, id, Employer, ModelState);
        if (!ModelState.IsValid) return Page();

        Employer.ApplyTo(existing);
        if (!await Db.TrySaveEditAsync(existing, Employer.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this employer after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Employer.RowVersion = existing.RowVersion;
            ModelState.Remove("Employer.RowVersion");
            return Page();
        }

        TempData["Message"] = "Employer updated.";
        return RedirectToIndex();
    }
}
