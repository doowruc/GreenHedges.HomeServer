using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Services;
using GreenHedges.HomeServer.Utilities.ViewModels;

namespace GreenHedges.HomeServer.Utilities.Pages.Meters;

/// <summary>
/// A meter's serial number and, for a replacement, when it was installed and what it showed. A meter without readings
/// can be deleted here.
/// </summary>
public class EditModel(AppDbContext db) : UtilityPageModel(db)
{
    [BindProperty]
    public MeterInput Meter { get; set; } = new();

    public string UnitSymbol { get; private set; } = string.Empty;

    /// <summary>Whether it has an installed date and start reading: a utility's first meter here has neither.</summary>
    public bool Replacement { get; private set; }

    public int Readings { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!LoadUtility()) return NotFound();
        var meter = await Db.Meters.Where(m => m.Id == id && m.Utility == Type)
            .Select(m => new
            {
                m.Unit, Readings = m.Readings.Count,
                Input = new MeterInput { InstalledOn = m.InstalledOn, StartReading = m.StartReading, SerialNumber = m.SerialNumber, RowVersion = m.RowVersion }
            })
            .FirstOrDefaultAsync();
        if (meter is null) return NotFound();
        Meter = meter.Input;
        (UnitSymbol, Readings, Replacement) = (meter.Unit.Symbol(), meter.Readings, meter.Input.InstalledOn is not null);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!LoadUtility()) return NotFound();
        var existing = await Db.Meters.FirstOrDefaultAsync(m => m.Id == id && m.Utility == Type);
        if (existing is null) return NotFound();
        (UnitSymbol, Replacement) = (existing.Unit.Symbol(), existing.InstalledOn is not null);
        Readings = await Db.Readings.CountAsync(r => r.MeterId == id);

        if (Replacement)
        {
            await MeterRules.CheckAsync(Db, Type, Meter, id, Today, ModelState);
            if (!ModelState.IsValid) return Page();
            existing.InstalledOn = Meter.InstalledOn;
            existing.StartReading = Meter.StartReading;
        }
        else if (!ModelState.IsValid) return Page();
        existing.SerialNumber = Meter.TidySerialNumber;

        if (!await Db.TrySaveEditAsync(existing, Meter.RowVersion))
        {
            if (Db.Entry(existing).State == EntityState.Detached) return NotFound();
            ModelState.AddModelError(string.Empty,
                "Someone changed this meter after you opened it, so your changes haven't been saved. " +
                "Reload the page to see the latest version, or save again to overwrite it with yours.");
            Meter.RowVersion = existing.RowVersion;
            ModelState.Remove("Meter.RowVersion");
            return Page();
        }

        TempData["Message"] = $"Updated the {Lower} meter.";
        return RedirectToUtility();
    }

    /// <summary>Deletes the meter if it has no readings (the database refuses otherwise, too).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!LoadUtility()) return NotFound();
        var deleted = await Db.Meters.Where(m => m.Id == id && m.Utility == Type && !m.Readings.Any()).ExecuteDeleteAsync();
        if (deleted == 0) TempData["Warning"] = "A meter with readings can't be deleted.";
        else TempData["Message"] = $"Deleted the {Lower} meter.";
        return RedirectToUtility();
    }
}
