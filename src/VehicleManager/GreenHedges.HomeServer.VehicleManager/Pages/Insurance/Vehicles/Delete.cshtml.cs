using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance.Vehicles;

public class DeleteModel(AppDbContext db) : InsurancePeriodPageModel(db)
{
    public InsurancePeriodView Period { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadPolicyAsync()) return NotFound();
        var period = await Db.InsurancePolicyVehicles.Where(v => v.Id == id && v.InsurancePolicyId == PolicyId)
            .Select(InsurancePeriodView.Projection).FirstOrDefaultAsync();
        if (period is null) return NotFound();
        Period = period;
        return Page();
    }

    /// <summary>Takes the vehicle off the policy (the cover's record, not the vehicle).</summary>
    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await Db.InsurancePolicyVehicles.Where(v => v.Id == id && v.InsurancePolicyId == PolicyId).ExecuteDeleteAsync() > 0)
            TempData["Message"] = "Vehicle removed from the policy.";
        return RedirectToPolicy();
    }
}
