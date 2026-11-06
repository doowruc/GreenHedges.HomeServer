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
/// licence by then, and their driver number ready to copy.
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

    /// <summary>The claims that need declaring under at least one cut-off, newest first.</summary>
    public List<ClaimRow> ToDeclare { get; private set; } = [];

    /// <summary>Older claims, outside every cut-off.</summary>
    public int OlderClaims { get; private set; }

    public string? DriverNumber { get; private set; }
    public DateOnly? LicenceIssueDate { get; private set; }
    public int? YearsHeld { get; private set; }
    public List<(string Name, string Value)>? Sections { get; private set; }

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
        RenewalDate = Date ?? DetectedRenewal ?? Reminders.UkToday();

        var claims = await db.Claims.Where(c => c.OwnerId == owner.Id).OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Select(ClaimView.Projection).ToListAsync();
        var rows = claims.Select(c => new ClaimRow(c, PreRenewal.CutOffYears.Select(y => PreRenewal.MustDeclare(c.Date, RenewalDate, y)).ToArray())).ToList();
        ToDeclare = rows.Where(r => r.Declare.Any(d => d)).ToList();
        OlderClaims = rows.Count(r => r.Claim.Date <= RenewalDate) - ToDeclare.Count;

        (DriverNumber, LicenceIssueDate) = (owner.DriverNumber, owner.LicenceIssueDate);
        YearsHeld = LicenceIssueDate is { } issued && issued <= RenewalDate ? PreRenewal.FullYears(issued, RenewalDate) : null;
        Sections = PreRenewal.Sections(DriverNumber);
        return Page();
    }
}
