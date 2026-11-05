using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles.Insurance;

public class DeleteModel(AppDbContext db) : InsurancePageModel(db)
{
    public InsurancePolicyView Policy { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadVehicleAsync()) return NotFound();
        var policy = await Db.InsurancePolicies.Where(p => p.Id == id && p.VehicleId == VehicleId)
            .Select(InsurancePolicyView.Projection).FirstOrDefaultAsync();
        if (policy is null) return NotFound();
        Policy = policy;
        return Page();
    }

    /// <summary>Deletes the policy. The renewal date follows: it's the end of whichever policy is now the latest.</summary>
    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await Db.InsurancePolicies.Where(p => p.Id == id && p.VehicleId == VehicleId).ExecuteDeleteAsync() > 0)
            TempData["Message"] = "Policy deleted.";
        return RedirectToVehicle();
    }
}
