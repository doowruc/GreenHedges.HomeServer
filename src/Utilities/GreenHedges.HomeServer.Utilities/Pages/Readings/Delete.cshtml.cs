using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Services;

namespace GreenHedges.HomeServer.Utilities.Pages.Readings;

public class DeleteModel(AppDbContext db) : UtilityPageModel(db)
{
    public string Description { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!LoadUtility()) return NotFound();
        var reading = await Db.Readings.Where(r => r.Id == id && r.Meter!.Utility == Type)
            .Select(r => new { r.Date, r.Value, r.Meter!.Unit }).FirstOrDefaultAsync();
        if (reading is null) return NotFound();
        Description = $"the {Lower} reading for {reading.Date:d}, {UtilityUnits.FormatUnits(reading.Value)} {reading.Unit.Symbol()}";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!LoadUtility()) return NotFound();
        await Db.Readings.Where(r => r.Id == id && r.Meter!.Utility == Type).ExecuteDeleteAsync();
        TempData["Message"] = "Reading deleted.";
        return RedirectToUtility();
    }
}
