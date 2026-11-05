using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance.Vehicles;

/// <summary>
/// Base for the pages that change which vehicles a policy covers, routed under <c>/Insurance/{policyId}/Vehicles</c>.
/// Links and redirects between them must pass <c>policyId</c> (ASP.NET doesn't carry it over to a different page).
/// </summary>
public abstract class InsurancePeriodPageModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int PolicyId { get; set; }

    public string PolicyName { get; private set; } = string.Empty;
    public DateTime PolicyStart { get; private set; }
    public DateTime PolicyEnd { get; private set; }

    protected AppDbContext Db => db;

    /// <summary>Loads the policy from the URL, and the vehicles to choose from (<c>ViewData["Vehicles"]</c>); false if it isn't one of the user's.</summary>
    protected async Task<bool> LoadPolicyAsync()
    {
        var policy = await db.InsurancePolicies.Where(p => p.Id == PolicyId)
            .Select(p => new { p.Provider, p.PolicyNumber, p.StartDate, p.EndDate }).FirstOrDefaultAsync();
        if (policy is null) return false;
        PolicyName = $"{policy.Provider} {policy.PolicyNumber}";
        (PolicyStart, PolicyEnd) = (policy.StartDate, policy.EndDate);
        ViewData["Vehicles"] = await InsuranceFormPageModel.VehicleOptionsAsync(db);
        return true;
    }

    /// <summary>Checks the vehicle is the user's and the cover fits the policy, adding any errors to the form.</summary>
    protected async Task ValidateAsync(InsurancePeriodInput input)
    {
        if (!ModelState.IsValid) return;
        if (!await db.Vehicles.AnyAsync(v => v.Id == input.VehicleId))
            ModelState.AddModelError("Period.VehicleId", "Choose one of your vehicles.");
        foreach (var error in input.OutsidePolicy(PolicyStart, PolicyEnd))
            ModelState.AddModelError(string.Empty, error);
    }

    /// <summary>Back to the policy's page.</summary>
    protected RedirectToPageResult RedirectToPolicy() => RedirectToPage("/Insurance/Details", new { id = PolicyId });
}
