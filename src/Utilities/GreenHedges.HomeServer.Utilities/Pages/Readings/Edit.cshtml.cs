using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Services;
using GreenHedges.HomeServer.Utilities.ViewModels;

namespace GreenHedges.HomeServer.Utilities.Pages.Readings;

public class EditModel(AppDbContext db) : UtilityPageModel(db)
{
    [BindProperty]
    public ReadingInput Reading { get; set; } = new();

    public string UnitSymbol { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!LoadUtility()) return NotFound();
        var reading = await Db.Readings.Where(r => r.Id == id && r.Meter!.Utility == Type)
            .Select(r => new { r.Meter!.Unit, Input = new ReadingInput { Date = r.Date, Value = r.Value, RowVersion = r.RowVersion } })
            .FirstOrDefaultAsync();
        if (reading is null) return NotFound();
        Reading = reading.Input;
        UnitSymbol = reading.Unit.Symbol();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!LoadUtility()) return NotFound();
        var existing = await Db.Readings.Include(r => r.Meter).FirstOrDefaultAsync(r => r.Id == id && r.Meter!.Utility == Type);
        if (existing is null) return NotFound();
        UnitSymbol = existing.Meter!.Unit.Symbol();

        var meterId = await ReadingRules.CheckAsync(Db, Type, Reading, id, Today, ModelState);
        if (!ModelState.IsValid || meterId is null) return Page();

        existing.Date = Reading.Date;
        existing.Value = Reading.Value!.Value;
        existing.MeterId = meterId.Value;   // a changed date can move it to the meter before or after
        if (!await Db.TrySaveEditAsync(existing, Reading.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this reading after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Reading.RowVersion = existing.RowVersion;
            ModelState.Remove("Reading.RowVersion");
            return Page();
        }

        TempData["Message"] = $"Updated the {Lower} reading for {existing.Date:d}.";
        return RedirectToUtility();
    }
}
