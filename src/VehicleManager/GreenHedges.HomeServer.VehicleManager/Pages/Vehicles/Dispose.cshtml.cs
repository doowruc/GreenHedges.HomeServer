using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles;

/// <summary>
/// Records how and when a vehicle was disposed of (or changes it), or undoes it (<c>?handler=Undo</c>). From that date
/// it has no reminders or lookups and isn't offered for new fill-ups or policies; its history stays.
/// </summary>
public class DisposeModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public DisposalInput Disposal { get; set; } = new();

    public string VehicleName { get; private set; } = string.Empty;
    public bool AlreadyDisposed { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var vehicle = await db.Vehicles.Where(v => v.Id == id)
            .Select(v => new { v.Make, v.Model, v.RegistrationNumber, v.DisposalMethod, v.DisposedDate, v.DisposalPrice, v.RowVersion })
            .FirstOrDefaultAsync();
        if (vehicle is null) return NotFound();
        VehicleName = $"{vehicle.Make} {vehicle.Model} ({vehicle.RegistrationNumber})";
        AlreadyDisposed = vehicle.DisposedDate is not null;
        Disposal = new DisposalInput
        {
            Method = vehicle.DisposalMethod, Date = vehicle.DisposedDate ?? Reminders.UkToday(), Price = vehicle.DisposalPrice,
            RowVersion = vehicle.RowVersion
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (existing is null) return NotFound();
        VehicleName = $"{existing.Make} {existing.Model} ({existing.RegistrationNumber})";
        AlreadyDisposed = existing.DisposedDate is not null;

        if (Disposal.Date is { } date)
        {
            if (existing.PurchasedDate is { } bought && date < bought)
                ModelState.AddModelError("Disposal.Date", $"It can't go before it was bought ({bought:dd/MM/yyyy}).");
            var lastFillUp = await db.FuelEntries.Where(f => f.VehicleId == id).MaxAsync(f => (DateOnly?)f.Date);
            if (lastFillUp > date)
                ModelState.AddModelError("Disposal.Date", $"It was filled up after then (on {lastFillUp:dd/MM/yyyy}).");
        }
        if (!ModelState.IsValid) return Page();

        Disposal.ApplyTo(existing);
        if (!await db.TrySaveEditAsync(existing, Disposal.RowVersion))
        {
            if (db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this vehicle after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Disposal.RowVersion = existing.RowVersion;
            ModelState.Remove("Disposal.RowVersion");
            return Page();
        }

        TempData["Message"] = $"{VehicleName}: {VehicleView.DisposalNames(existing.DisposalMethod!.Value).ToLowerInvariant()} {existing.DisposedDate:dd/MM/yyyy}.";
        return RedirectToPage("Details", new { id });
    }

    /// <summary>Puts the vehicle back as a current one (e.g. recorded by mistake), clearing how, when and the price.</summary>
    public async Task<IActionResult> OnPostUndoAsync(int id)
    {
        var existing = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (existing is null) return NotFound();
        new DisposalInput().ApplyTo(existing);
        await db.SaveChangesAsync();
        TempData["Message"] = $"{existing.Make} {existing.Model} ({existing.RegistrationNumber}) is a current vehicle again.";
        return RedirectToPage("Details", new { id });
    }
}
