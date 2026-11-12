using Microsoft.AspNetCore.Mvc;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Services;
using GreenHedges.HomeServer.Utilities.ViewModels;

namespace GreenHedges.HomeServer.Utilities.Pages.Readings;

/// <summary>A utility's page: the form to add a reading, its readings with their figures worked out (latest first), and its meters.</summary>
public class IndexModel(AppDbContext db, UtilityReadings readings) : UtilityPageModel(db)
{
    [BindProperty]
    public ReadingInput Reading { get; set; } = new();

    public UtilityHistory History { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!LoadUtility()) return NotFound();
        History = await readings.LoadAsync(Type);
        Reading.Date = Today;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!LoadUtility()) return NotFound();

        var meterId = await ReadingRules.CheckAsync(Db, Type, Reading, null, Today, ModelState);
        if (!ModelState.IsValid || meterId is null)
        {
            History = await readings.LoadAsync(Type);
            return Page();
        }

        var reading = new Reading { Date = Reading.Date, Value = Reading.Value!.Value };
        if (meterId == 0)
            // The utility's first reading: its first meter comes with it, with no known start.
            Db.Meters.Add(new Meter { Utility = Type, Unit = Type.DefaultUnit(), Readings = [reading] });
        else
        {
            reading.MeterId = meterId.Value;
            Db.Readings.Add(reading);
        }
        await Db.SaveChangesAsync();

        TempData["Message"] = $"Added the {Lower} reading for {reading.Date:d}.";
        return RedirectToUtility();
    }
}
