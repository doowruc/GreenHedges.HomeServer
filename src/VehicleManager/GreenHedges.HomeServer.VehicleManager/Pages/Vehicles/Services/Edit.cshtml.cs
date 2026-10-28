using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles.Services;

public class EditModel(AppDbContext db) : ServicePageModel(db)
{
    [BindProperty]
    public ServiceRecordInput Service { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadVehicleAsync()) return NotFound();
        var service = await Db.ServiceRecords.Where(s => s.Id == id && s.VehicleId == VehicleId)
            .Select(ServiceRecordInput.Projection).FirstOrDefaultAsync();
        if (service is null) return NotFound();
        Service = service;
        await LoadSuggestionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadVehicleAsync()) return NotFound();
        var existing = await Db.ServiceRecords.FirstOrDefaultAsync(s => s.Id == id && s.VehicleId == VehicleId);
        if (existing is null) return NotFound();
        if (!ModelState.IsValid)
        {
            await LoadSuggestionsAsync();
            return Page();
        }

        // Only a changed date or mileage moves the next service, so other edits keep any due date set by hand.
        var moved = existing.Date != Service.Date || existing.Mileage != Service.Mileage;
        Service.ApplyTo(existing);
        var next = moved ? await ServiceSchedule.UpdateNextDueAsync(Db, await Db.Vehicles.FirstAsync(v => v.Id == VehicleId), existing) : null;
        if (!await Db.TrySaveEditAsync(existing, Service.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this service after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Service.RowVersion = existing.RowVersion;
            ModelState.Remove("Service.RowVersion");
            await LoadSuggestionsAsync();
            return Page();
        }

        TempData["Message"] = "Service updated." + (next is null ? "" : " " + next);
        return RedirectToVehicle();
    }
}
