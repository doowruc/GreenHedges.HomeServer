using Microsoft.AspNetCore.Mvc;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Services;
using GreenHedges.HomeServer.Utilities.ViewModels;

namespace GreenHedges.HomeServer.Utilities.Pages.Meters;

/// <summary>A replacement meter: readings from its installed date on are its own, and its first counts units from its start reading.</summary>
public class CreateModel(AppDbContext db) : UtilityPageModel(db)
{
    [BindProperty]
    public MeterInput Meter { get; set; } = new();

    public string UnitSymbol => Type.DefaultUnit().Symbol();

    public IActionResult OnGet()
    {
        if (!LoadUtility()) return NotFound();
        Meter.InstalledOn = Today;
        Meter.StartReading = 0;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!LoadUtility()) return NotFound();
        await MeterRules.CheckAsync(Db, Type, Meter, null, Today, ModelState);
        if (!ModelState.IsValid) return Page();

        Db.Meters.Add(new Meter
        {
            Utility = Type, Unit = Type.DefaultUnit(), InstalledOn = Meter.InstalledOn, StartReading = Meter.StartReading,
            SerialNumber = Meter.TidySerialNumber
        });
        await Db.SaveChangesAsync();
        TempData["Message"] = $"Added the {Lower} meter installed on {Meter.InstalledOn:d}.";
        return RedirectToUtility();
    }
}
