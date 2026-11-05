using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;
using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Vehicles;

public class DetailsModel(AppDbContext db, FuelStatsService stats, VehicleLookup lookup) : PageModel
{
    public VehicleView Vehicle { get; private set; } = new();
    public List<FuelEntryView> Entries { get; private set; } = [];
    public FuelSummary AllTime { get; private set; } = FuelSummary.Empty;
    public FuelSummary LastYear { get; private set; } = FuelSummary.Empty;
    public string ChartJson { get; private set; } = "[]";

    /// <summary>Mileage charts' readings (for wwwroot/js/mileage-chart.js), oldest first; null with fewer than two to draw.</summary>
    public string? ServiceChartJson { get; private set; }
    public string? MotChartJson { get; private set; }
    public List<DueDateView> DueDates { get; private set; } = [];
    public MileageTrend? Mileage { get; private set; }
    public List<ServiceRecordView> Services { get; private set; } = [];
    /// <summary>The services of the last 12 months, for the period toggle.</summary>
    public List<ServiceRecordView> ServicesLastYear { get; private set; } = [];
    public List<MotTestView> MotTests { get; private set; } = [];
    /// <summary>The insurance policies that have covered this vehicle, latest-ending first (with all their vehicles, for its share of each).</summary>
    public List<InsurancePolicyView> Policies { get; private set; } = [];
    /// <summary>How many spells the vehicle has had on them (a policy it was switched off and back on to counts twice).</summary>
    public int InsuranceSpells { get; private set; }
    /// <summary>This vehicle's insurance costs for wwwroot/js/premium-chart.js (InsuranceCosts.ChartJson); null with fewer than two.</summary>
    public string? PremiumChartJson { get; private set; }
    /// <summary>The MOT tests of the last 12 months, for the period toggle.</summary>
    public List<MotTestView> MotTestsLastYear { get; private set; } = [];

    /// <summary>A service due at a mileage whose date can't be estimated yet (too few odometer readings).</summary>
    public int? UnestimatedServiceMileage { get; private set; }

    /// <summary>The DVLA or DVSA is set up, so MOT (and tax, with the DVLA) can be checked.</summary>
    public bool CanLookUp => lookup.IsAvailable;

    /// <summary>
    /// The header offers each check only while its date is missing: the vehicle hasn't been checked yet, or nothing was
    /// found. Otherwise the MOT is checked from the MOT tab. Either service has MOT dates; only the DVLA has tax.
    /// </summary>
    public bool ShowHeaderMotCheck => CanLookUp && Vehicle.MotDueDate is null;
    public bool ShowHeaderTaxCheck => lookup.HasTax && Vehicle.TaxDueDate is null;

    /// <summary>The <c>check</c> values a lookup takes: just the MOT or just the tax (neither means both).</summary>
    public const string CheckMot = "mot", CheckTax = "tax";

    /// <summary>The DVSA is set up, so checks bring in the MOT history.</summary>
    public bool HasMotHistory => lookup.HasMotHistory;

    /// <summary>The MOT tab shows with the DVSA set up, or MOT tests stored from when it was.</summary>
    public bool ShowMotTab => HasMotHistory || MotTests.Count > 0;

    /// <summary>All vehicles for the switcher; each option's value is that vehicle's details URL, on the current tab.</summary>
    public List<SelectListItem> VehicleOptions { get; private set; } = [];

    /// <summary>The tab shown first: fuel (the default), services, insurance or MOT, from <c>?tab=</c>.</summary>
    public string Tab { get; private set; } = FuelTab;
    public const string FuelTab = "fuel", ServicesTab = "services", InsuranceTab = "insurance", MotTab = "mot";

    private static bool IsOtherTab(string? tab) => tab is ServicesTab or InsuranceTab or MotTab;

    public async Task<IActionResult> OnGetAsync(int id, string? tab)
    {
        Tab = IsOtherTab(tab) ? tab! : FuelTab;
        var vehicle = await db.Vehicles.Where(v => v.Id == id).Select(VehicleView.Projection).FirstOrDefaultAsync();
        if (vehicle is null) return NotFound();
        Vehicle = vehicle;

        Entries = await db.FuelEntries
            .Where(f => f.VehicleId == id)
            .OrderByDescending(f => f.Date).ThenByDescending(f => f.Id)
            .Select(FuelEntryView.Projection)
            .ToListAsync();

        Services = await db.ServiceRecords
            .Where(s => s.VehicleId == id)
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.Id)
            .Select(ServiceRecordView.Projection)
            .ToListAsync();
        ServiceSchedule.SetDue(Services, vehicle.ServiceIntervalMonths, vehicle.ServiceIntervalMiles);

        Policies = await db.InsurancePolicies
            .Where(p => p.Vehicles.Any(v => v.VehicleId == id))
            .OrderByDescending(p => p.EndDate).ThenByDescending(p => p.Id)
            .Select(InsurancePolicyView.Projection)
            .ToListAsync();
        InsurancePolicyView.MarkCurrent(Policies, Reminders.UkNow());
        InsuranceSpells = Policies.Sum(p => p.Vehicles.Count(v => v.VehicleId == id));
        PremiumChartJson = InsuranceCosts.ChartJson(Policies, id);

        MotTests = MotTestView.NewestFirst(await db.MotTests
            .Where(t => t.VehicleId == id)
            .Select(MotTestView.Projection)
            .ToListAsync());
        var yearAgo = Reminders.UkToday().AddYears(-1);
        MotTestView.MarkPreviousOwner(MotTests, vehicle.PurchasedDate);
        MotTestsLastYear = MotTests.Where(t => t.Date >= yearAgo).ToList();
        ServicesLastYear = Services.Where(s => s.Date >= yearAgo).ToList();   // late/over badges already set from the full history
        if (Tab == MotTab && !ShowMotTab) Tab = FuelTab;

        // Switching vehicle keeps the tab (the script below updates these links when the tab changes).
        VehicleOptions = (await db.Vehicles.OrderBy(v => v.Make).ThenBy(v => v.Model)
                .Select(v => new { v.Id, v.Make, v.Model, v.RegistrationNumber }).ToListAsync())
            .Select(v => new SelectListItem(
                $"{v.Make} {v.Model} ({v.RegistrationNumber})", Url.Page("Details", new { id = v.Id, tab = Tab == FuelTab ? null : Tab }), v.Id == id))
            .ToList();

        Mileage = MileageTrend.From(Entries, MotTests.Where(t => t.Miles is not null).Select(t => (t.Date, t.Miles!.Value)));
        DueDates = vehicle.DueDates(Reminders.UkToday(), Mileage);
        if (Mileage is null) UnestimatedServiceMileage = vehicle.ServiceDueMileage;

        AllTime = stats.Summarise(Entries);
        LastYear = stats.Summarise(Entries, DateOnly.FromDateTime(DateTime.Today.AddYears(-1)));

        ChartJson = JsonSerializer.Serialize(
            Entries.AsEnumerable().Reverse().Select(f => new
            {
                date = f.Date.ToString("dd/MM/yy"),
                mpg = f.CalculatedMpg,
                dash = f.TripAverageMpg,
                price = f.EffectiveCostPerLitre
            }));

        ServiceChartJson = MileageChartJson(Services.AsEnumerable().Reverse()
            .Select(s => (s.Date, s.Mileage, s.GarageAndLocation, false, s.UnderPreviousOwner, (string?)null)));
        MotChartJson = MileageChartJson(MotTests.AsEnumerable().Reverse().Where(t => t.Miles is not null)
            .Select(t => (t.Date, t.Miles!.Value, t.Passed ? "Pass" : "Fail", !t.Passed, t.BoughtAfter is not null, (string?)MotTestView.ChartRef(t.Id))));

        return Page();
    }

    /// <param name="readings">Each with an optional <c>Ref</c>: clicking its point scrolls to the item with that <c>data-chart-ref</c>.</param>
    private static string? MileageChartJson(IEnumerable<(DateOnly Date, int Miles, string Note, bool Failed, bool PreviousOwner, string? Ref)> readings)
    {
        var list = readings.ToList();
        return list.Count < 2 ? null : JsonSerializer.Serialize(list.Select(r => new
        {
            date = r.Date.ToString("yyyy-MM-dd"),
            miles = r.Miles,
            note = r.Note,
            failed = r.Failed,
            previousOwner = r.PreviousOwner,
            @ref = r.Ref
        }));
    }

    /// <summary>
    /// Checks the MOT due date (and MOT history), the tax due date, or both (<paramref name="check"/>: mot, tax or
    /// neither) with whichever of the DVLA and DVSA are set up, and saves any found. Returns to the tab it was asked from.
    /// </summary>
    public async Task<IActionResult> OnPostLookupAsync(int id, string? tab, string? check)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id);
        if (vehicle is null) return NotFound();
        var parts = check switch { CheckMot => LookupParts.Mot, CheckTax => LookupParts.Tax, _ => LookupParts.All };
        if (!lookup.IsAvailable || (parts == LookupParts.Tax && !lookup.HasTax)) return BadRequest();

        var found = await lookup.RefreshAsync(vehicle, parts);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Checked {vehicle.RegistrationNumber} with {found.CheckedWith}: {found.Summary}.";
        if (found.Problems.Count > 0) TempData["Warning"] = string.Join(" ", found.Problems);
        return RedirectToPage(new { id, tab = IsOtherTab(tab) ? tab : null });
    }
}
