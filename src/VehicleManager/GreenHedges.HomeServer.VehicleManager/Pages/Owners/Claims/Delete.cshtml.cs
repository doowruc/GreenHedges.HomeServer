using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Owners.Claims;

public class DeleteModel(AppDbContext db) : ClaimPageModel(db)
{
    public ClaimView Claim { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadOwnerAsync()) return NotFound();
        var claim = await Db.Claims.Where(c => c.Id == id && c.OwnerId == OwnerId).Select(ClaimView.Projection).FirstOrDefaultAsync();
        if (claim is null) return NotFound();
        Claim = claim;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await Db.Claims.Where(c => c.Id == id && c.OwnerId == OwnerId).ExecuteDeleteAsync() > 0)
            TempData["Message"] = "Claim deleted.";
        return RedirectToOwner();
    }
}
