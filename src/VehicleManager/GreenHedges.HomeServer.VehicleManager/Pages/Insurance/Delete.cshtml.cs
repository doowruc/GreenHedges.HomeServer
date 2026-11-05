using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance;

public class DeleteModel(AppDbContext db) : PageModel
{
    public InsurancePolicyView Policy { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var policy = await db.InsurancePolicies.Where(p => p.Id == id).Select(InsurancePolicyView.Projection).FirstOrDefaultAsync();
        if (policy is null) return NotFound();
        Policy = policy;
        return Page();
    }

    /// <summary>Deletes the policy and its vehicles' cover. Their renewal dates follow: the end of whichever policy is now their latest.</summary>
    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await db.InsurancePolicies.Where(p => p.Id == id).ExecuteDeleteAsync() > 0)
            TempData["Message"] = "Policy deleted.";
        return RedirectToPage("Index");
    }
}
