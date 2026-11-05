using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.FuelLog;

/// <summary>Shared plumbing for the Create and Edit fill-up pages.</summary>
public abstract class FuelFormPageModel(AppDbContext db, FuelStops stops) : PageModel
{
    /// <summary>The previous fill-up's date and odometer reading.</summary>
    public record PreviousFillUp(DateOnly Date, int? Odometer);

    protected AppDbContext Db => db;
    protected FuelStops Stops => stops;

    [BindProperty]
    public FuelEntryInput Entry { get; set; } = new();

    public List<SelectListItem> VehicleOptions { get; private set; } = [];
    public List<string> Brands { get; private set; } = [];

    /// <summary>
    /// Brand name (lower case) → its station names. The form suggests only the typed brand's stations,
    /// and none for a new brand.
    /// </summary>
    public Dictionary<string, List<string>> StationsByBrand { get; private set; } = [];

    /// <summary>The fill-up before this one for the selected vehicle (for odometer hints).</summary>
    public PreviousFillUp? PreviousEntry { get; private set; }

    /// <param name="editingId">The fill-up being edited, so it isn't treated as its own previous fill-up.</param>
    protected async Task LoadLookupsAsync(int? editingId = null)
    {
        // Not vehicles disposed of, except the one already chosen (editing an old fill-up).
        var current = Vehicle.CurrentOn(Reminders.UkToday());
        VehicleOptions = await db.Vehicles.Where(current).Union(db.Vehicles.Where(v => v.Id == Entry.VehicleId))
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(v => new SelectListItem($"{v.Make} {v.Model} ({v.RegistrationNumber})", v.Id.ToString()))
            .ToListAsync();

        Brands = await db.FuelBrands.OrderBy(b => b.Name).Select(b => b.Name).ToListAsync();
        var stations = await db.FuelStations.OrderBy(s => s.Name)
            .Select(s => new { s.Name, Brand = s.FuelBrand!.Name }).ToListAsync();
        StationsByBrand = stations.GroupBy(s => s.Brand.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Select(s => s.Name).ToList());

        // The form always shows at least one brand/station row.
        if (Entry.Stops.Count == 0) Entry.Stops.Add(new FuelStopInput());

        if (Entry.VehicleId != 0)
        {
            PreviousEntry = await db.FuelEntries
                .Where(f => f.VehicleId == Entry.VehicleId && f.Id != editingId && f.Date <= Entry.Date)
                .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
                .Select(f => new PreviousFillUp(f.Date, f.Odometer))
                .FirstOrDefaultAsync();
        }
    }

    protected async Task ValidateAsync()
    {
        var vehicle = await db.Vehicles.Where(v => v.Id == Entry.VehicleId)
            .Select(v => new { v.DisposalMethod, v.DisposedDate }).FirstOrDefaultAsync();
        if (vehicle is null)
            ModelState.AddModelError("Entry.VehicleId", "Choose a vehicle.");
        else if (vehicle.DisposedDate is { } gone && Entry.Date > gone)
            ModelState.AddModelError("Entry.Date", $"The vehicle was {VehicleView.DisposalNames(vehicle.DisposalMethod!.Value).ToLowerInvariant()} on {gone:dd/MM/yyyy}.");
        if (Entry.VoucherPerLitre is { } voucher && voucher >= Entry.CostPerLitre)
            ModelState.AddModelError("Entry.VoucherPerLitre", "Voucher discount must be less than the price per litre.");
        if (Entry.Date > DateOnly.FromDateTime(DateTime.Today.AddDays(1)))
            ModelState.AddModelError("Entry.Date", "Date can't be in the future.");
        await stops.ValidateAsync(Entry.Stops, ModelState, "Entry.Stops");
    }
}
