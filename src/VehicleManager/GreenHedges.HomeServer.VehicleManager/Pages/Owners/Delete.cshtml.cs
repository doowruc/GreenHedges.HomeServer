using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Pages.Owners;

public class DeleteModel(AppDbContext db) : PageModel
{
    public string OwnerName { get; private set; } = string.Empty;

    /// <summary>Vehicles still owned; the owner can't be deleted until they are reassigned.</summary>
    public List<VehicleView> Vehicles { get; private set; } = [];

    /// <summary>Insurance policies they hold; the owner can't be deleted until these are deleted or given to someone else.</summary>
    public int PolicyCount { get; private set; }

    /// <summary>Insurance claims they've made; the owner can't be deleted until these are deleted.</summary>
    public int ClaimCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var name = await db.Owners.Where(o => o.Id == id).Select(o => o.Name).FirstOrDefaultAsync();
        if (name is null) return NotFound();
        OwnerName = name;
        Vehicles = await db.Vehicles.Where(v => v.OwnerId == id)
            .OrderBy(v => v.Make).ThenBy(v => v.Model)
            .Select(VehicleView.Projection)
            .ToListAsync();
        PolicyCount = await db.InsurancePolicies.CountAsync(p => p.OwnerId == id);
        ClaimCount = await db.Claims.CountAsync(c => c.OwnerId == id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var name = await db.Owners.Where(o => o.Id == id).Select(o => o.Name).FirstOrDefaultAsync();
        if (name is null) return RedirectToPage("Index");
        if (await db.Vehicles.AnyAsync(v => v.OwnerId == id) || await db.InsurancePolicies.AnyAsync(p => p.OwnerId == id)
            || await db.Claims.AnyAsync(c => c.OwnerId == id))
            return RedirectToPage("Delete", new { id });

        await db.Owners.Where(o => o.Id == id).ExecuteDeleteAsync();
        TempData["Message"] = $"Deleted {name}.";
        return RedirectToPage("Index");
    }
}
