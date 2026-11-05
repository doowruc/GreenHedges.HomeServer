using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Insurance;

/// <summary>Every insurance policy (or one owner's, <c>?ownerId=</c>), with each vehicle's insurance cost over time.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? OwnerId { get; set; }

    public List<InsurancePolicyView> Policies { get; private set; } = [];
    public List<SelectListItem> OwnerOptions { get; private set; } = [];

    /// <summary>For wwwroot/js/premium-chart.js (InsuranceCosts.ChartJson); null with fewer than two points.</summary>
    public string? ChartJson { get; private set; }

    public async Task OnGetAsync()
    {
        OwnerOptions = (await db.Owners.OrderBy(o => o.Name).Select(o => new { o.Id, o.Name }).ToListAsync())
            .Select(o => new SelectListItem(o.Name, o.Id.ToString(), o.Id == OwnerId)).ToList();
        Policies = await db.InsurancePolicies
            .Where(p => OwnerId == null || p.OwnerId == OwnerId)
            .OrderByDescending(p => p.StartDate).ThenByDescending(p => p.Id)
            .Select(InsurancePolicyView.Projection)
            .ToListAsync();
        InsurancePolicyView.MarkCurrent(Policies, Reminders.UkNow());
        ChartJson = InsuranceCosts.ChartJson(Policies);
    }
}
