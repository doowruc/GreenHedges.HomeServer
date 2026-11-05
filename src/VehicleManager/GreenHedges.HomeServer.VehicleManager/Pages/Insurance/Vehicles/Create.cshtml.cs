using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance.Vehicles;

/// <summary>
/// Puts a vehicle on the policy: alongside the others (a multi-car policy), or switching from one (<c>?replace=</c>, that
/// vehicle's cover), which ends the old vehicle's cover when the new one's starts.
/// </summary>
public class CreateModel(AppDbContext db) : InsurancePeriodPageModel(db)
{
    [BindProperty]
    public InsurancePeriodInput Period { get; set; } = new();

    /// <summary>The cover being switched from, if any.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Replace { get; set; }

    /// <summary>The vehicle being switched from, e.g. "Ford Focus (AB12 CDE)".</summary>
    public string? ReplacedVehicle { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPolicyAsync()) return NotFound();
        var (from, to) = (PolicyStart, PolicyEnd);
        if (Replace is not null)
        {
            if (await LoadReplacedAsync() is not { } replaced) return NotFound();
            (from, to) = (replaced.From.AddMinutes(1), replaced.To);
        }
        // From now (to the minute), if that's within the cover; a whole policy that's yet to start, from its start.
        var now = InsurancePolicyInput.ToMinute(Reminders.UkNow());
        Period.From = now < from ? from : now >= to ? from : now;
        Period.To = to;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadPolicyAsync()) return NotFound();
        InsurancePolicyVehicle? replaced = null;
        if (Replace is { } replaceId)
        {
            replaced = await Db.InsurancePolicyVehicles.FirstOrDefaultAsync(v => v.Id == replaceId && v.InsurancePolicyId == PolicyId);
            if (replaced is null) return NotFound();
            await LoadReplacedAsync();
        }
        await ValidateAsync(Period);
        if (ModelState.IsValid && replaced is not null)
        {
            var from = InsurancePolicyInput.ToMinute(Period.From!.Value);
            if (from <= replaced.From || from >= replaced.To)
                ModelState.AddModelError("Period.From",
                    $"The switch must be while the vehicle it replaces is covered ({replaced.From:dd/MM/yyyy HH:mm} to {replaced.To:dd/MM/yyyy HH:mm}).");
            if (Period.VehicleId == replaced.VehicleId) ModelState.AddModelError("Period.VehicleId", "Choose the vehicle it's switching to.");
        }
        if (!ModelState.IsValid) return Page();

        var period = new InsurancePolicyVehicle { InsurancePolicyId = PolicyId };
        Period.ApplyTo(period);
        if (replaced is not null) replaced.To = period.From;
        Db.InsurancePolicyVehicles.Add(period);
        await Db.SaveChangesAsync();

        TempData["Message"] = replaced is null ? "Vehicle added to the policy." : $"Switched vehicle from {period.From:dd/MM/yyyy HH:mm}.";
        return RedirectToPolicy();
    }

    private async Task<InsurancePeriodView?> LoadReplacedAsync()
    {
        var replaced = await Db.InsurancePolicyVehicles.Where(v => v.Id == Replace && v.InsurancePolicyId == PolicyId)
            .Select(InsurancePeriodView.Projection).FirstOrDefaultAsync();
        ReplacedVehicle = replaced?.VehicleLabel;
        return replaced;
    }
}
