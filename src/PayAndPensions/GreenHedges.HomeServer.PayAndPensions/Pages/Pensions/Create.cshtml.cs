using Microsoft.AspNetCore.Mvc;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Pensions;

public class CreateModel(AppDbContext db) : PensionFormModel(db)
{
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        await LoadProvidersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();
        await PensionRules.ValidateAsync(Db, PersonId, null, Pension, ModelState);
        if (!ModelState.IsValid)
        {
            await LoadProvidersAsync();
            return Page();
        }

        var pension = new Pension { PersonId = PersonId };
        Pension.ApplyTo(pension);
        Db.Pensions.Add(pension);
        await Db.SaveChangesAsync();
        TempData["Message"] = $"Added {pension.Name}.";
        return RedirectToIndex();
    }
}
