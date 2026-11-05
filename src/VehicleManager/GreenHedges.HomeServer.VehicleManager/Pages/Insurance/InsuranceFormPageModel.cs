using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Services;
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
