using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance;

/// <summary>
/// Adds a policy for one vehicle (covered for the whole policy), or renews a policy (<c>?renew=</c>): starting where it
/// ends, for the vehicles on it then. From a vehicle's page (<c>?vehicleId=</c>) it starts from that vehicle's latest policy.
/// </summary>
public class CreateModel(AppDbContext db) : InsuranceFormPageModel(db)
{
    [BindProperty]
    public InsurancePolicyInput Policy { get; set; } = new();

    /// <summary>The policy being renewed, if any.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Renew { get; set; }

    /// <summary>The vehicles a renewal covers (the renewed policy's at its end).</summary>
    public List<InsurancePeriodView> RenewedVehicles { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? vehicleId)
    {
        InsurancePolicyView? from = null;
        if (Renew is { } renew)
        {
            from = await Db.InsurancePolicies.Where(p => p.Id == renew).Select(InsurancePolicyView.Projection).FirstOrDefaultAsync();
            if (from is null) return NotFound();
            RenewedVehicles = from.VehiclesAtEnd.ToList();
        }
        else if (vehicleId is { } id)
        {
            var ownerId = await Db.Vehicles.Where(v => v.Id == id).Select(v => (int?)v.OwnerId).FirstOrDefaultAsync();
            if (ownerId is null) return NotFound();
            from = await Db.InsurancePolicies.Where(p => p.Vehicles.Any(v => v.VehicleId == id))
                .OrderByDescending(p => p.EndDate).ThenByDescending(p => p.Id)
                .Select(InsurancePolicyView.Projection).FirstOrDefaultAsync();
            Policy.VehicleId = id;
            Policy.OwnerId = from?.OwnerId ?? ownerId;
        }

        // A renewal starts when the policy before ends, with its insurer, cover, excess and drivers; otherwise from now.
        var start = from is not null ? RenewalStart(from.EndDate) : InsurancePolicyInput.ToMinute(Reminders.UkNow());
        Policy.StartDate = start;
        Policy.EndDate = DefaultEnd(start);
        if (from is not null)
        {
            Policy.OwnerId = from.OwnerId;
            Policy.Provider = from.Provider;
            Policy.Cover = from.Cover;
            Policy.Excess = from.Excess;
            Policy.NamedDrivers = from.NamedDrivers;
        }
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        List<int> vehicleIds;
        if (Renew is { } renew)
        {
            var renewed = await Db.InsurancePolicies.Where(p => p.Id == renew).Select(InsurancePolicyView.Projection).FirstOrDefaultAsync();
            if (renewed is null) return NotFound();
            RenewedVehicles = renewed.VehiclesAtEnd.ToList();
            vehicleIds = RenewedVehicles.Select(v => v.VehicleId).Distinct().ToList();
        }
        else
        {
            if (Policy.VehicleId is null) ModelState.AddModelError("Policy.VehicleId", "Choose the vehicle it covers.");
            else if (!await Db.Vehicles.AnyAsync(v => v.Id == Policy.VehicleId)) ModelState.AddModelError("Policy.VehicleId", "Choose one of your vehicles.");
            vehicleIds = Policy.VehicleId is { } id ? [id] : [];
        }
        if (Policy.OwnerId is { } ownerId && !await Db.Owners.AnyAsync(o => o.Id == ownerId))
            ModelState.AddModelError("Policy.OwnerId", "Choose one of your owners.");
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        var policy = new InsurancePolicy();
        Policy.ApplyTo(policy);
        foreach (var vehicleId in vehicleIds)
            policy.Vehicles.Add(new InsurancePolicyVehicle { VehicleId = vehicleId, From = policy.StartDate, To = policy.EndDate });
        Db.InsurancePolicies.Add(policy);
        await Db.SaveChangesAsync();

        TempData["Message"] = $"Added the {policy.Provider} policy, {policy.StartDate:dd/MM/yyyy HH:mm} to {policy.EndDate:dd/MM/yyyy HH:mm}.";
        return RedirectToPage("Details", new { id = policy.Id });
    }

    /// <summary>Where a renewal starts: when the policy before ends, or the minute after an end at 23:59 (cover runs to the end of that minute).</summary>
    public static DateTime RenewalStart(DateTime previousEnd) =>
        previousEnd.TimeOfDay == new TimeSpan(23, 59, 0) ? previousEnd.AddMinutes(1) : previousEnd;

    /// <summary>A year's cover: to 23:59 the day before the anniversary for a policy starting at midnight, else to the same time a year on.</summary>
    public static DateTime DefaultEnd(DateTime start) =>
        start.TimeOfDay == TimeSpan.Zero ? start.AddYears(1).AddMinutes(-1) : start.AddYears(1);
}
