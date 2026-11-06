using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Owners.Claims;

/// <summary>
/// Base for an owner's claim pages, routed under <c>/Owners/{ownerId}/Claims</c>. Links and redirects between them must
/// pass <c>ownerId</c> (ASP.NET doesn't carry it over to a different page).
/// </summary>
public abstract class ClaimPageModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int OwnerId { get; set; }

    public string OwnerName { get; private set; } = string.Empty;

    protected AppDbContext Db => db;

    /// <summary>
    /// Loads the owner from the URL and the choices: every vehicle (claims are history, so previous ones too) and the
    /// owner's policies, with when each covered which vehicle so the form can suggest one. False if it isn't the user's.
    /// </summary>
    protected async Task<bool> LoadOwnerAsync()
    {
        var name = await db.Owners.Where(o => o.Id == OwnerId).Select(o => o.Name).FirstOrDefaultAsync();
        if (name is null) return false;
        OwnerName = name;
        ViewData["Vehicles"] = (await db.Vehicles.OrderBy(v => v.Make).ThenBy(v => v.Model)
                .Select(v => new { v.Id, v.Make, v.Model, v.RegistrationNumber }).ToListAsync())
            .Select(v => new SelectListItem($"{v.Make} {v.Model} ({v.RegistrationNumber})".Trim(), v.Id.ToString())).ToList();
        var policies = await db.InsurancePolicies.Where(p => p.OwnerId == OwnerId).OrderByDescending(p => p.StartDate)
            .Select(p => new
            {
                p.Id, p.Provider, p.PolicyNumber, p.StartDate, p.EndDate,
                Periods = p.Vehicles.Select(v => new { v.VehicleId, v.From, v.To }).ToList()
            }).ToListAsync();
        ViewData["Policies"] = policies
            .Select(p => new SelectListItem($"{p.Provider} {p.PolicyNumber} ({p.StartDate:dd/MM/yyyy} to {p.EndDate:dd/MM/yyyy})", p.Id.ToString())).ToList();
        ViewData["PolicyCover"] = JsonSerializer.Serialize(policies.Select(p => new
        {
            id = p.Id, start = DateOnly.FromDateTime(p.StartDate), end = InsurancePolicyView.LastDay(p.EndDate),
            periods = p.Periods.Select(v => new { vehicle = v.VehicleId, from = DateOnly.FromDateTime(v.From), to = InsurancePolicyView.LastDay(v.To) })
        }));
        return true;
    }

    /// <summary>Checks the vehicle is the user's and the policy is the owner's and covers the date, adding any errors to the form.</summary>
    protected async Task ValidateAsync(ClaimInput input)
    {
        if (input.VehicleId is { } vehicleId && !await db.Vehicles.AnyAsync(v => v.Id == vehicleId))
            ModelState.AddModelError("Claim.VehicleId", "Choose one of your vehicles.");
        if (input.InsurancePolicyId is { } policyId)
        {
            var policy = await db.InsurancePolicies.Where(p => p.Id == policyId && p.OwnerId == OwnerId)
                .Select(p => new { p.StartDate, p.EndDate }).FirstOrDefaultAsync();
            if (policy is null)
                ModelState.AddModelError("Claim.InsurancePolicyId", $"Choose one of {OwnerName}'s policies.");
            else if (input.Date is { } date && (date < DateOnly.FromDateTime(policy.StartDate) || date > InsurancePolicyView.LastDay(policy.EndDate)))
                ModelState.AddModelError("Claim.InsurancePolicyId",
                    $"That policy runs {policy.StartDate:dd/MM/yyyy} to {policy.EndDate:dd/MM/yyyy}, so it doesn't cover the claim's date.");
        }
    }

    /// <summary>Back to the owner's page.</summary>
    protected RedirectToPageResult RedirectToOwner() => RedirectToPage("/Owners/Details", new { id = OwnerId });
}
