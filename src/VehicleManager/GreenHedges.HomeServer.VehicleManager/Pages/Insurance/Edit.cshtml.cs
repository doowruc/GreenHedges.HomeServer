using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance;

/// <summary>
/// Edits a policy's details. Its vehicles are changed on its own page; moving its start or end moves the vehicles' cover
/// that started or ended with it, and anything else must still fit inside.
/// </summary>
public class EditModel(AppDbContext db) : InsuranceFormPageModel(db)
{
    [BindProperty]
    public InsurancePolicyInput Policy { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var policy = await Db.InsurancePolicies.Where(p => p.Id == id).Select(InsurancePolicyInput.Projection).FirstOrDefaultAsync();
        if (policy is null) return NotFound();
        Policy = policy;
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var existing = await Db.InsurancePolicies.Include(p => p.Vehicles).ThenInclude(v => v.Vehicle).Include(p => p.Drivers).FirstOrDefaultAsync(p => p.Id == id);
        if (existing is null) return NotFound();
        if (Policy.OwnerId is { } ownerId && !await Db.Owners.AnyAsync(o => o.Id == ownerId))
            ModelState.AddModelError("Policy.OwnerId", "Choose one of your owners.");
        await ValidateDriversAsync(Policy);
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        var (oldStart, oldEnd) = (existing.StartDate, existing.EndDate);
        Policy.ApplyTo(existing);
        SetDrivers(existing, Policy.DriverIds);
        foreach (var v in existing.Vehicles)
        {
            if (v.From == oldStart) v.From = existing.StartDate;
            if (v.To == oldEnd) v.To = existing.EndDate;
            if (v.From < existing.StartDate || v.To > existing.EndDate || v.To <= v.From)
                ModelState.AddModelError(string.Empty,
                    $"The {v.Vehicle!.Make} {v.Vehicle.Model}'s cover ({v.From:dd/MM/yyyy HH:mm} to {v.To:dd/MM/yyyy HH:mm}) wouldn't fit " +
                    "the new dates. Change it on the policy's page first.");
        }
        if (!ModelState.IsValid)
        {
            Db.ChangeTracker.Clear();
            await LoadOptionsAsync();
            return Page();
        }

        if (!await Db.TrySaveEditAsync(existing, Policy.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this policy after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Policy.RowVersion = existing.RowVersion;
            ModelState.Remove("Policy.RowVersion");
            await LoadOptionsAsync();
            return Page();
        }

        TempData["Message"] = "Policy updated.";
        return RedirectToPage("Details", new { id });
    }
}
