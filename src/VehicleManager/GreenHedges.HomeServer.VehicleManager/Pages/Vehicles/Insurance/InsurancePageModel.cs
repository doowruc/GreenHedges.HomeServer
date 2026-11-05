using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles.Insurance;

/// <summary>
/// Base for a vehicle's insurance policy pages, routed under <c>/Vehicles/{vehicleId}/Insurance</c>.
/// Links and redirects between them must pass <c>vehicleId</c> (ASP.NET doesn't carry it over to a
/// different page, and the link comes out empty without it).
/// </summary>
public abstract class InsurancePageModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int VehicleId { get; set; }

    public string VehicleName { get; private set; } = string.Empty;
    public string RegistrationNumber { get; private set; } = string.Empty;

    protected AppDbContext Db => db;

    /// <summary>Loads the vehicle from the URL; false if it isn't one of the user's.</summary>
    protected async Task<bool> LoadVehicleAsync()
    {
        var vehicle = await db.Vehicles.Where(v => v.Id == VehicleId)
            .Select(v => new { v.Make, v.Model, v.RegistrationNumber }).FirstOrDefaultAsync();
        if (vehicle is null) return false;
        VehicleName = $"{vehicle.Make} {vehicle.Model}".Trim();
        RegistrationNumber = vehicle.RegistrationNumber;
        return true;
    }

    /// <summary>Every insurer used on any vehicle's policies, for <c>_InsuranceForm</c>'s suggestions (<c>ViewData["Providers"]</c>).</summary>
    protected async Task LoadSuggestionsAsync()
    {
        var used = await db.InsurancePolicies.Select(p => p.Provider).Distinct().ToListAsync();
        ViewData["Providers"] = JsonSerializer.Serialize(used.Distinct(StringComparer.OrdinalIgnoreCase).Order());
    }

    /// <summary>Back to the vehicle's page, on its Insurance tab.</summary>
    protected RedirectToPageResult RedirectToVehicle() => RedirectToPage("/Vehicles/Details", new { id = VehicleId, tab = DetailsModel.InsuranceTab });
}
