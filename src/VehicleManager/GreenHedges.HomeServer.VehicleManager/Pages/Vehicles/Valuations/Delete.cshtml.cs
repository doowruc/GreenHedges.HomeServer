using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles.Valuations;

public class DeleteModel(AppDbContext db) : ValuationPageModel(db)
{
    public VehicleValuationView Valuation { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadVehicleAsync()) return NotFound();
        var valuation = await Db.VehicleValuations.Where(v => v.Id == id && v.VehicleId == VehicleId)
            .Select(VehicleValuationView.Projection).FirstOrDefaultAsync();
        if (valuation is null) return NotFound();
        Valuation = valuation;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await Db.VehicleValuations.Where(v => v.Id == id && v.VehicleId == VehicleId).ExecuteDeleteAsync() > 0)
            TempData["Message"] = "Value deleted.";
        return RedirectToVehicle();
    }
}
