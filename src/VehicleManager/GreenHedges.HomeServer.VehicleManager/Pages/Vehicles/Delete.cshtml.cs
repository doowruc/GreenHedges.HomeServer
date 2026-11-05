using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles;

public class DeleteModel(AppDbContext db) : PageModel
{
    public VehicleView Vehicle { get; private set; } = new();
    public int FillUps { get; private set; }
    public int Services { get; private set; }
    public int Claims { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var vehicle = await db.Vehicles.Where(v => v.Id == id).Select(VehicleView.Projection).FirstOrDefaultAsync();
        if (vehicle is null) return NotFound();
        Vehicle = vehicle;
        FillUps = await db.FuelEntries.CountAsync(f => f.VehicleId == id);
        Services = await db.ServiceRecords.CountAsync(s => s.VehicleId == id);
        Claims = await db.Claims.CountAsync(c => c.VehicleId == id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var name = await db.Vehicles.Where(v => v.Id == id).Select(v => v.Make + " " + v.Model).FirstOrDefaultAsync();
        if (name is not null)
        {
            // Its fill-ups, services and claims go too, via the cascade delete on the foreign keys.
            await db.Vehicles.Where(v => v.Id == id).ExecuteDeleteAsync();
            TempData["Message"] = $"Deleted {name}.";
        }
        return RedirectToPage("Index");
    }
}
