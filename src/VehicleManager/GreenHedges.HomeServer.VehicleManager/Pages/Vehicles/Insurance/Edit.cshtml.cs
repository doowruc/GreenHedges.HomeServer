using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles.Insurance;

public class EditModel(AppDbContext db) : InsurancePageModel(db)
{
    [BindProperty]
    public InsurancePolicyInput Policy { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadVehicleAsync()) return NotFound();
        var policy = await Db.InsurancePolicies.Where(p => p.Id == id && p.VehicleId == VehicleId)
            .Select(InsurancePolicyInput.Projection).FirstOrDefaultAsync();
        if (policy is null) return NotFound();
        Policy = policy;
        await LoadSuggestionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadVehicleAsync()) return NotFound();
        var existing = await Db.InsurancePolicies.FirstOrDefaultAsync(p => p.Id == id && p.VehicleId == VehicleId);
        if (existing is null) return NotFound();
        if (!ModelState.IsValid)
        {
            await LoadSuggestionsAsync();
            return Page();
        }

        Policy.ApplyTo(existing);
        if (!await Db.TrySaveEditAsync(existing, Policy.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this policy after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Policy.RowVersion = existing.RowVersion;
            ModelState.Remove("Policy.RowVersion");
            await LoadSuggestionsAsync();
            return Page();
        }

        TempData["Message"] = "Policy updated.";
        return RedirectToVehicle();
    }
}
