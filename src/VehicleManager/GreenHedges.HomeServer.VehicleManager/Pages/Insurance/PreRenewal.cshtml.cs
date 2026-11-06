using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance;

/// <summary>
/// What to have to hand for a renewal quote, for one owner (<c>?ownerId=</c>, else the first): the renewal date (from
/// their current policy, or <c>?date=</c>), the claims to declare for 3, 4 and 5 year cut-offs, how long they've held a
/// licence by then, and their driver number ready to copy; and the same for each owner who's a named driver on the policy.
/// </summary>
public class PreRenewalModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? OwnerId { get; set; }

    /// <summary>A renewal date typed in, instead of the current policy's.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? Date { get; set; }

    public List<SelectListItem> OwnerOptions { get; private set; } = [];
    public string OwnerName { get; private set; } = string.Empty;

    /// <summary>The owner's policy in force now, if any: its renewal date is the one detected.</summary>
    public InsurancePolicyView? CurrentPolicy { get; private set; }
    public DateOnly? DetectedRenewal { get; private set; }

    /// <summary>The date everything's worked out for: <see cref="Date"/>, else the detected one, else today.</summary>
    public DateOnly RenewalDate { get; private set; }

    public record ClaimRow(ClaimView Claim, bool[] Declare);

    /// <summary>
    /// What's asked about one driver: their licence, the claims they must declare under at least one cut-off (newest
    /// first), and how many older ones needn't be.
    /// </summary>
    public record Person(int OwnerId, string Name, bool IsPolicyholder, string? DriverNumber, DateOnly? LicenceIssueDate, int? YearsHeld,
        List<ClaimRow> ToDeclare, int OlderClaims)
    {
        public List<(string Name, string Value)>? Sections => PreRenewal.Sections(DriverNumber);
    }

    /// <summary>The policyholder, then the owners who are named drivers on the policy being renewed.</summary>
    public List<Person> People { get; private set; } = [];

    /// <summary>The policy whose named drivers are included: the current one, else the latest to have started.</summary>
    public InsurancePolicyView? BasisPolicy { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var owners = await db.Owners.OrderBy(o => o.Name).Select(o => new { o.Id, o.Name, o.DriverNumber, o.LicenceIssueDate }).ToListAsync();
        if (owners.Count == 0) return Page();
        var owner = owners.FirstOrDefault(o => o.Id == OwnerId) ?? (OwnerId is null ? owners[0] : null);
        if (owner is null) return NotFound();
        OwnerId = owner.Id;
        OwnerName = owner.Name;
        OwnerOptions = owners.Select(o => new SelectListItem(o.Name, o.Id.ToString(), o.Id == owner.Id)).ToList();

        var now = Reminders.UkNow();
        var policies = await db.InsurancePolicies.Where(p => p.OwnerId == owner.Id).Select(InsurancePolicyView.Projection).ToListAsync();
        DetectedRenewal = PreRenewal.RenewalDate(policies.Select(p => (p.StartDate, p.EndDate)), now);
        CurrentPolicy = policies.Where(p => p.StartDate <= now && now < p.EndDate).MaxBy(p => p.EndDate);
        BasisPolicy = CurrentPolicy ?? policies.Where(p => p.StartDate <= now).MaxBy(p => p.EndDate);
        RenewalDate = Date ?? DetectedRenewal ?? Reminders.UkToday();

        var ids = (BasisPolicy?.Drivers.Select(d => d.OwnerId) ?? []).Prepend(owner.Id).Distinct().ToList();
        var claims = (await db.Claims.Where(c => ids.Contains(c.OwnerId)).OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Select(ClaimView.Projection).ToListAsync()).ToLookup(c => c.OwnerId);
        People = ids.Select(id => owners.Single(o => o.Id == id)).Select(o =>
        {
            var rows = claims[o.Id].Select(c => new ClaimRow(c, PreRenewal.CutOffYears.Select(y => PreRenewal.MustDeclare(c.Date, RenewalDate, y)).ToArray())).ToList();
            var toDeclare = rows.Where(r => r.Declare.Any(d => d)).ToList();
            var years = o.LicenceIssueDate is { } issued && issued <= RenewalDate ? PreRenewal.FullYears(issued, RenewalDate) : (int?)null;
            return new Person(o.Id, o.Name, o.Id == owner.Id, o.DriverNumber, o.LicenceIssueDate, years, toDeclare,
                rows.Count(r => r.Claim.Date <= RenewalDate) - toDeclare.Count);
        }).ToList();
        return Page();
    }
}
