using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Owners;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public OwnerInput Owner { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var owner = await db.Owners.Where(o => o.Id == id).Select(OwnerInput.Projection).FirstOrDefaultAsync();
        if (owner is null) return NotFound();
        Owner = owner;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await db.Owners.FirstOrDefaultAsync(o => o.Id == id);
        if (existing is null) return NotFound();

        await OwnerRules.ValidateAsync(db, id, Owner, ModelState);
        if (!ModelState.IsValid) return Page();

        Owner.ApplyTo(existing);
        if (!await db.TrySaveEditAsync(existing, Owner.RowVersion))
        {
            if (db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this owner after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Owner.RowVersion = existing.RowVersion;
            ModelState.Remove("Owner.RowVersion");
            return Page();
        }

        TempData["Message"] = "Owner updated.";
        return RedirectToPage("Index");
    }
}
