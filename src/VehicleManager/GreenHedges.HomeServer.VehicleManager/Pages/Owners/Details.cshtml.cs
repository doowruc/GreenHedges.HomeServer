using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Owners;

/// <summary>An owner: their driving licence, vehicles and insurance claims (added here, at <c>Claims/*</c>).</summary>
public class DetailsModel(AppDbContext db) : PageModel
{
    /// <summary>How far back insurers usually ask about claims.</summary>
    public const int ClaimYears = 5;

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? DriverNumber { get; private set; }
    public DateOnly? LicenceIssueDate { get; private set; }
    public int? YearsHeld { get; private set; }
    public List<VehicleView> Vehicles { get; private set; } = [];
    public List<ClaimView> Claims { get; private set; } = [];
    public DateOnly Today { get; private set; }

    /// <summary>Claims within the last <see cref="ClaimYears"/> years, and how many of those were at least partly the owner's fault.</summary>
    public int RecentClaims { get; private set; }
    public int RecentAtFault { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var owner = await db.Owners.Where(o => o.Id == id)
            .Select(o => new { o.Id, o.Name, o.DriverNumber, o.LicenceIssueDate }).FirstOrDefaultAsync();
        if (owner is null) return NotFound();
        (Id, Name, DriverNumber, LicenceIssueDate) = (owner.Id, owner.Name, owner.DriverNumber, owner.LicenceIssueDate);
        Today = Reminders.UkToday();
        YearsHeld = new IndexModel.Row(Id, Name, 0, DriverNumber, LicenceIssueDate).YearsHeld(Today);

        Vehicles = await db.Vehicles.Where(v => v.OwnerId == id).OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(VehicleView.Projection).ToListAsync();
        Claims = await db.Claims.Where(c => c.OwnerId == id).OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Select(ClaimView.Projection).ToListAsync();
        var since = Today.AddYears(-ClaimYears);
        var recent = Claims.Where(c => c.Date > since).ToList();
        (RecentClaims, RecentAtFault) = (recent.Count, recent.Count(c => c.Fault != ClaimFault.OtherParty));
        return Page();
    }
}
