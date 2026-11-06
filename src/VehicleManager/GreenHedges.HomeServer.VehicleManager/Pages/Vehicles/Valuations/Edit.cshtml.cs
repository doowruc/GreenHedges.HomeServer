using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles.Valuations;

public class EditModel(AppDbContext db) : ValuationPageModel(db)
{
    [BindProperty]
    public VehicleValuationInput Valuation { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadVehicleAsync()) return NotFound();
        var valuation = await Db.VehicleValuations.Where(v => v.Id == id && v.VehicleId == VehicleId)
            .Select(VehicleValuationInput.Projection).FirstOrDefaultAsync();
        if (valuation is null) return NotFound();
        Valuation = valuation;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadVehicleAsync()) return NotFound();
        var existing = await Db.VehicleValuations.FirstOrDefaultAsync(v => v.Id == id && v.VehicleId == VehicleId);
        if (existing is null) return NotFound();
        if (Valuation.Date is { } date && DisposedDate is { } disposed && date > disposed)
            ModelState.AddModelError("Valuation.Date", $"The vehicle was disposed of on {disposed:dd/MM/yyyy}.");
        if (!ModelState.IsValid) return Page();

        Valuation.ApplyTo(existing);
        if (!await Db.TrySaveEditAsync(existing, Valuation.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this value after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Valuation.RowVersion = existing.RowVersion;
            ModelState.Remove("Valuation.RowVersion");
            return Page();
        }

        TempData["Message"] = "Value updated.";
        return RedirectToVehicle();
    }
}
