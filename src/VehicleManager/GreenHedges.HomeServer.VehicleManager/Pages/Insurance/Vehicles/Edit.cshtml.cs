using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance.Vehicles;

public class EditModel(AppDbContext db) : InsurancePeriodPageModel(db)
{
    [BindProperty]
    public InsurancePeriodInput Period { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPolicyAsync()) return NotFound();
        var period = await Db.InsurancePolicyVehicles.Where(v => v.Id == id && v.InsurancePolicyId == PolicyId)
            .Select(InsurancePeriodInput.Projection).FirstOrDefaultAsync();
        if (period is null) return NotFound();
        Period = period;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadPolicyAsync()) return NotFound();
        var existing = await Db.InsurancePolicyVehicles.FirstOrDefaultAsync(v => v.Id == id && v.InsurancePolicyId == PolicyId);
        if (existing is null) return NotFound();
        await ValidateAsync(Period);
        if (!ModelState.IsValid) return Page();

        Period.ApplyTo(existing);
        if (!await Db.TrySaveEditAsync(existing, Period.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this vehicle's cover after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Period.RowVersion = existing.RowVersion;
            ModelState.Remove("Period.RowVersion");
            return Page();
        }

        TempData["Message"] = "Vehicle's cover updated.";
        return RedirectToPolicy();
    }
}
