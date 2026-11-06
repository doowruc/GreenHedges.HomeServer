using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles.Valuations;

/// <summary>
/// Base for a vehicle's value pages, routed under <c>/Vehicles/{vehicleId}/Valuations</c> (values are added on the
/// pre-renewal page). Links and redirects between them must pass <c>vehicleId</c>.
/// </summary>
public abstract class ValuationPageModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int VehicleId { get; set; }

    public string VehicleName { get; private set; } = string.Empty;
    public string RegistrationNumber { get; private set; } = string.Empty;

    /// <summary>When the vehicle was disposed of, if it has been: a value can't be after it.</summary>
    public DateOnly? DisposedDate { get; private set; }

    protected AppDbContext Db => db;

    /// <summary>Loads the vehicle from the URL; false if it isn't one of the user's.</summary>
    protected async Task<bool> LoadVehicleAsync()
    {
        var vehicle = await db.Vehicles.Where(v => v.Id == VehicleId)
            .Select(v => new { v.Make, v.Model, v.RegistrationNumber, v.DisposedDate }).FirstOrDefaultAsync();
        if (vehicle is null) return false;
        VehicleName = $"{vehicle.Make} {vehicle.Model}".Trim();
        RegistrationNumber = vehicle.RegistrationNumber;
        DisposedDate = vehicle.DisposedDate;
        return true;
    }

    /// <summary>Back to the vehicle's page, on its Insurance tab (where its values are listed).</summary>
    protected RedirectToPageResult RedirectToVehicle() =>
        RedirectToPage("/Vehicles/Details", null, new { id = VehicleId, tab = DetailsModel.InsuranceTab }, "vehicle-values");
}
