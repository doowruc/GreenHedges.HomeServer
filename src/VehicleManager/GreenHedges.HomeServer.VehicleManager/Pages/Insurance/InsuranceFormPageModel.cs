using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Data;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance;

/// <summary>Base for the policy form pages (add, edit): the choices <c>_InsuranceForm</c> reads from <c>ViewData</c>.</summary>
public abstract class InsuranceFormPageModel(AppDbContext db) : PageModel
{
    protected AppDbContext Db => db;

    /// <summary>
    /// The owners (<c>"Owners"</c>) and vehicles (<c>"Vehicles"</c>) to choose from, and every insurer used before
    /// (<c>"Providers"</c>, for suggestions).
    /// </summary>
    /// <param name="vehicleId">The vehicle already chosen, listed even if it's been disposed of.</param>
    protected async Task LoadOptionsAsync(int? vehicleId = null)
    {
        ViewData["Owners"] = (await db.Owners.OrderBy(o => o.Name).Select(o => new { o.Id, o.Name }).ToListAsync())
            .Select(o => new SelectListItem(o.Name, o.Id.ToString())).ToList();
        ViewData["Vehicles"] = await VehicleOptionsAsync(db, vehicleId is { } id ? [id] : null);
        var used = await db.InsurancePolicies.Select(p => p.Provider).Distinct().ToListAsync();
        ViewData["Providers"] = JsonSerializer.Serialize(used.Distinct(StringComparer.OrdinalIgnoreCase).Order());
    }

    /// <summary>
    /// Checks the named drivers are the user's owners, dropping the policyholder (they're not their own named driver).
    /// </summary>
    protected async Task ValidateDriversAsync(InsurancePolicyInput input)
    {
        input.DriverIds = input.DriverIds.Distinct().Where(id => id != input.OwnerId).ToList();
        var ids = input.DriverIds;
        if (await db.Owners.CountAsync(o => ids.Contains(o.Id)) != ids.Count)
            ModelState.AddModelError("Policy.DriverIds", "Choose named drivers from your owners.");
    }

    /// <summary>Makes the policy's owner named drivers <paramref name="ownerIds"/> (call after <see cref="ValidateDriversAsync"/>; load its drivers first).</summary>
    protected static void SetDrivers(InsurancePolicy policy, IReadOnlyCollection<int> ownerIds)
    {
        policy.Drivers.RemoveAll(d => !ownerIds.Contains(d.OwnerId));
        foreach (var id in ownerIds.Where(id => policy.Drivers.All(d => d.OwnerId != id)))
            policy.Drivers.Add(new InsurancePolicyDriver { OwnerId = id });
    }

    /// <summary>
    /// The user's vehicles, e.g. "Ford Focus (AB12 CDE)": not those disposed of, except <paramref name="keep"/> (the one
    /// already chosen).
    /// </summary>
    public static async Task<List<SelectListItem>> VehicleOptionsAsync(AppDbContext db, IReadOnlyCollection<int>? keep = null)
    {
        var kept = keep ?? [];
        return (await db.Vehicles.Where(Vehicle.CurrentOn(Reminders.UkToday())).Union(db.Vehicles.Where(v => kept.Contains(v.Id)))
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(v => new { v.Id, v.Make, v.Model, v.RegistrationNumber }).ToListAsync())
        .Select(v => new SelectListItem($"{v.Make} {v.Model} ({v.RegistrationNumber})".Trim(), v.Id.ToString())).ToList();
    }
}
