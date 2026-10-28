using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelLog;

public class EditModel(AppDbContext db, FuelStops stops) : FuelFormPageModel(db, stops)
{
    public async Task<IActionResult> OnGetAsync(int id)
    {
        var entry = await Db.FuelEntries.Where(f => f.Id == id).Select(FuelEntryInput.Projection).FirstOrDefaultAsync();
        if (entry is null) return NotFound();
        Entry = entry;
        await LoadLookupsAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await Db.FuelEntries.Include(f => f.Stops).FirstOrDefaultAsync(f => f.Id == id);
        if (existing is null) return NotFound();

        await ValidateAsync();
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync(id);
            return Page();
        }

        Entry.ApplyTo(existing);
        await Stops.SetStopsAsync(existing, Entry.Stops);
        if (!await Db.TrySaveEditAsync(existing, Entry.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this fill-up after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Entry.RowVersion = existing.RowVersion;
            ModelState.Remove("Entry.RowVersion");
            await LoadLookupsAsync(id);
            return Page();
        }
        TempData["Message"] = "Fill-up updated.";
        return RedirectToPage("/Vehicles/Details", new { id = Entry.VehicleId });
    }
}
