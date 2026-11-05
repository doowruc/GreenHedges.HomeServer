using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance;

/// <summary>A policy and the vehicles it covers, where vehicles are switched, added, changed and removed.</summary>
public class DetailsModel(AppDbContext db) : PageModel
{
    public InsurancePolicyView Policy { get; private set; } = new();

    /// <summary>The time now in the UK, for the vehicles' "Current" badges.</summary>
    public DateTime Now { get; } = Reminders.UkNow();

    /// <summary>Claims made on the policy, newest first.</summary>
    public List<ClaimView> Claims { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var policy = await db.InsurancePolicies.Where(p => p.Id == id).Select(InsurancePolicyView.Projection).FirstOrDefaultAsync();
        if (policy is null) return NotFound();
        InsurancePolicyView.MarkCurrent([policy], Now);
        Policy = policy;
        Claims = await db.Claims.Where(c => c.InsurancePolicyId == id).OrderByDescending(c => c.Date)
            .Select(ClaimView.Projection).ToListAsync();
        return Page();
    }
}
