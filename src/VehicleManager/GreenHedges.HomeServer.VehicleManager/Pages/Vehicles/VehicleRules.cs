using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles;

internal static class VehicleRules
{
    public static string NormaliseReg(string? reg) =>
        string.Join(' ', (reg ?? string.Empty).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Normalises the input and checks it against the other vehicles (<paramref name="id"/> is null for a new one).</summary>
    public static async Task ValidateAsync(AppDbContext db, int? id, VehicleInput v, ModelStateDictionary state)
    {
        v.RegistrationNumber = NormaliseReg(v.RegistrationNumber);
        if (await db.Vehicles.AnyAsync(x => x.Id != id && x.RegistrationNumber == v.RegistrationNumber))
            state.AddModelError("Vehicle.RegistrationNumber", id is null
                ? "A vehicle with this registration already exists."
                : "Another vehicle already has this registration.");

        if (v.RegistrationDate is { } reg && v.PurchasedDate is { } bought && bought < reg)
            state.AddModelError("Vehicle.PurchasedDate", "Purchased date can't be before the registration date.");
        if (id is not null && v.PurchasedDate is { } purchased
            && await db.Vehicles.Where(x => x.Id == id).Select(x => x.DisposedDate).FirstOrDefaultAsync() is { } gone && purchased > gone)
            state.AddModelError("Vehicle.PurchasedDate", $"Purchased date can't be after it went ({gone:dd/MM/yyyy}).");

        if (!await db.Owners.AnyAsync(o => o.Id == v.OwnerId))
            state.AddModelError("Vehicle.OwnerId", "Choose an owner.");
    }

    /// <summary>Owner choices for <c>_VehicleForm</c>, which reads them from <c>ViewData["OwnerOptions"]</c>.</summary>
    public static async Task<List<SelectListItem>> OwnerOptionsAsync(AppDbContext db) =>
        await db.Owners.OrderBy(o => o.Name)
            .Select(o => new SelectListItem(o.Name, o.Id.ToString()))
            .ToListAsync();
}
