using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Owners.Claims;

public class EditModel(AppDbContext db) : ClaimPageModel(db)
{
    [BindProperty]
    public ClaimInput Claim { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadOwnerAsync()) return NotFound();
        var claim = await Db.Claims.Where(c => c.Id == id && c.OwnerId == OwnerId).Select(ClaimInput.Projection).FirstOrDefaultAsync();
        if (claim is null) return NotFound();
        Claim = claim;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadOwnerAsync()) return NotFound();
        var existing = await Db.Claims.FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == OwnerId);
        if (existing is null) return NotFound();
        await ValidateAsync(Claim);
        if (!ModelState.IsValid) return Page();

        Claim.ApplyTo(existing);
        if (!await Db.TrySaveEditAsync(existing, Claim.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this claim after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Claim.RowVersion = existing.RowVersion;
            ModelState.Remove("Claim.RowVersion");
            return Page();
        }

        TempData["Message"] = "Claim updated.";
        return RedirectToOwner();
    }
}
