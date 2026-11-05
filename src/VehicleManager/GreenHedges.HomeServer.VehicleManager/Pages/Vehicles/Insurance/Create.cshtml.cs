using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles.Insurance;

public class CreateModel(AppDbContext db) : InsurancePageModel(db)
{
    [BindProperty]
    public InsurancePolicyInput Policy { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadVehicleAsync()) return NotFound();
        await LoadSuggestionsAsync();

        // A renewal: starting when the latest policy ends, with its insurer, cover, excess and drivers. Otherwise from now.
        var latest = await Db.InsurancePolicies.Where(p => p.VehicleId == VehicleId)
            .OrderByDescending(p => p.EndDate).ThenByDescending(p => p.Id)
            .Select(InsurancePolicyInput.Projection).FirstOrDefaultAsync();
        var start = latest?.EndDate is { } end ? RenewalStart(end) : InsurancePolicyInput.ToMinute(Reminders.UkNow());
        Policy = new InsurancePolicyInput
        {
            StartDate = start,
            EndDate = DefaultEnd(start),
            Provider = latest?.Provider ?? string.Empty,
            Cover = latest?.Cover ?? InsuranceCover.Comprehensive,
            Excess = latest?.Excess,
            NamedDrivers = latest?.NamedDrivers
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadVehicleAsync()) return NotFound();
        if (!ModelState.IsValid)
        {
            await LoadSuggestionsAsync();
            return Page();
        }

        var policy = new InsurancePolicy { VehicleId = VehicleId };
        Policy.ApplyTo(policy);
        Db.InsurancePolicies.Add(policy);
        await Db.SaveChangesAsync();

        TempData["Message"] = $"Added the {policy.Provider} policy, {policy.StartDate:dd/MM/yyyy HH:mm} to {policy.EndDate:dd/MM/yyyy HH:mm}.";
        return RedirectToVehicle();
    }

    /// <summary>Where a renewal starts: when the policy before ends, or the minute after an end at 23:59 (cover runs to the end of that minute).</summary>
    public static DateTime RenewalStart(DateTime previousEnd) =>
        previousEnd.TimeOfDay == new TimeSpan(23, 59, 0) ? previousEnd.AddMinutes(1) : previousEnd;

    /// <summary>A year's cover: to 23:59 the day before the anniversary for a policy starting at midnight, else to the same time a year on.</summary>
    public static DateTime DefaultEnd(DateTime start) =>
        start.TimeOfDay == TimeSpan.Zero ? start.AddYears(1).AddMinutes(-1) : start.AddYears(1);
}
