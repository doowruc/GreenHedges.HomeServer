using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles;

public class EditModel(AppDbContext db, VehicleLookup lookup) : PageModel
{
    [BindProperty]
    public VehicleInput Vehicle { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var vehicle = await db.Vehicles.Where(v => v.Id == id).Select(VehicleInput.Projection).FirstOrDefaultAsync();
        if (vehicle is null) return NotFound();
        Vehicle = vehicle;
        ViewData["OwnerOptions"] = await VehicleRules.OwnerOptionsAsync(db);
        ViewData["LookupNote"] = lookup.MorningCheck;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (existing is null) return NotFound();

        await VehicleRules.ValidateAsync(db, id, Vehicle, ModelState);
        if (!ModelState.IsValid)
        {
            ViewData["OwnerOptions"] = await VehicleRules.OwnerOptionsAsync(db);
            ViewData["LookupNote"] = lookup.MorningCheck;
            return Page();
        }

        Vehicle.ApplyTo(existing);
        if (!await db.TrySaveEditAsync(existing, Vehicle.RowVersion))
        {
            if (db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this vehicle after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Vehicle.RowVersion = existing.RowVersion;
            ModelState.Remove("Vehicle.RowVersion");
            ViewData["OwnerOptions"] = await VehicleRules.OwnerOptionsAsync(db);
            ViewData["LookupNote"] = lookup.MorningCheck;
            return Page();
        }

        TempData["Message"] = "Vehicle updated.";
        return RedirectToPage("Details", new { id });
    }
}
