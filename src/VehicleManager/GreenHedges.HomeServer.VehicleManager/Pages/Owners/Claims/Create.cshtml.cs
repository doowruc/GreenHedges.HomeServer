using Microsoft.AspNetCore.Mvc;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Owners.Claims;

public class CreateModel(AppDbContext db) : ClaimPageModel(db)
{
    [BindProperty]
    public ClaimInput Claim { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadOwnerAsync()) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadOwnerAsync()) return NotFound();
        await ValidateAsync(Claim);
        if (!ModelState.IsValid) return Page();

        var claim = new Models.Claim { OwnerId = OwnerId };
        Claim.ApplyTo(claim);
        Db.Claims.Add(claim);
        await Db.SaveChangesAsync();
        TempData["Message"] = $"Added the claim of {claim.Date:dd/MM/yyyy} ({claim.TotalValue:C}).";
        return RedirectToOwner();
    }
}
