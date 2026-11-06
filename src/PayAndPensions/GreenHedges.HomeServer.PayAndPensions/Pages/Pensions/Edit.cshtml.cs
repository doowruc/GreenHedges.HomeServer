using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.ViewModels;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Pensions;

public class EditModel(AppDbContext db) : PensionFormModel(db)
{
    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var pension = await Db.Pensions.Where(pn => pn.Id == id && pn.PersonId == PersonId)
            .Select(PensionInput.Projection).FirstOrDefaultAsync();
        if (pension is null) return NotFound();
        Pension = pension;
        await LoadProvidersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPersonAsync()) return NotFound();
        var existing = await Db.Pensions.FirstOrDefaultAsync(pn => pn.Id == id && pn.PersonId == PersonId);
        if (existing is null) return NotFound();

        await PensionRules.ValidateAsync(Db, PersonId, id, Pension, ModelState);
        if (!ModelState.IsValid)
        {
            await LoadProvidersAsync();
            return Page();
        }

        Pension.ApplyTo(existing);
        if (!await Db.TrySaveEditAsync(existing, Pension.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this pension after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Pension.RowVersion = existing.RowVersion;
            ModelState.Remove("Pension.RowVersion");
            await LoadProvidersAsync();
            return Page();
        }

        TempData["Message"] = "Pension updated.";
        return RedirectToIndex();
    }
}
