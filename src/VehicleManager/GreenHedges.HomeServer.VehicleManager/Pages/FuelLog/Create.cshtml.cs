using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelLog;

public class CreateModel(AppDbContext db, FuelStops stops) : FuelFormPageModel(db, stops)
{
    public async Task OnGetAsync(int? vehicleId)
    {
        // Default to the requested vehicle, otherwise the one most recently filled up that's still owned.
        var current = Vehicle.CurrentOn(Reminders.UkToday());
        Entry.VehicleId = vehicleId
            ?? await Db.FuelEntries.Where(f => Db.Vehicles.Where(current).Any(v => v.Id == f.VehicleId))
                    .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
                    .Select(f => (int?)f.VehicleId).FirstOrDefaultAsync()
            ?? await Db.Vehicles.Where(current).Select(v => (int?)v.Id).FirstOrDefaultAsync()
            ?? 0;

        // Brand and station start blank (picked with the form's suggestions each time).
        await LoadLookupsAsync();
    }

    public async Task<IActionResult> OnPostAsync(bool addAnother = false)
    {
        await ValidateAsync();
        if (!ModelState.IsValid)
        {
            await LoadLookupsAsync();
            return Page();
        }

        var entry = new FuelEntry();
        Entry.ApplyTo(entry);
        await Stops.SetStopsAsync(entry, Entry.Stops);
        Db.FuelEntries.Add(entry);
        await Db.SaveChangesAsync();
        TempData["Message"] = $"Fill-up on {Entry.Date:dd/MM/yyyy} saved ({Entry.TotalCost:C}).";

        return addAnother
            ? RedirectToPage("Create", new { vehicleId = Entry.VehicleId })
            : RedirectToPage("/Vehicles/Details", new { id = Entry.VehicleId });
    }
}
