using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Services;
using GreenHedges.HomeServer.Utilities.ViewModels;

namespace GreenHedges.HomeServer.Utilities.Pages.Meters;

/// <summary>What a replacement meter must satisfy, adding or editing, beyond its annotations.</summary>
public static class MeterRules
{
    /// <summary>
    /// A replacement needs its installed date and start reading, and every reading must stay on the meter it was
    /// taken from: a reading belongs to the latest meter installed on or before its date. Errors go in <paramref name="state"/>.
    /// </summary>
    /// <param name="meterId">The meter being edited; null for a new one.</param>
    public static async Task CheckAsync(AppDbContext db, UtilityType utility, MeterInput input, int? meterId, DateOnly today,
        ModelStateDictionary state)
    {
        if (input.InstalledOn is null) state.AddModelError("Meter.InstalledOn", "Enter the date it was installed.");
        if (input.StartReading is null) state.AddModelError("Meter.StartReading", "Enter what it showed when installed (usually 0).");
        if (input.InstalledOn is not { } installedOn || input.StartReading is not { } start) return;

        if (decimal.Round(start, 3) != start)
            state.AddModelError("Meter.StartReading", "A reading has at most three decimal places.");
        if (installedOn > today)
            state.AddModelError("Meter.InstalledOn", "The date can't be in the future.");
        if (!state.IsValid) return;

        var meters = await db.Meters.Where(m => m.Utility == utility && m.Id != meterId)
            .Select(m => new MeterInfo(m.Id, m.InstalledOn, m.StartReading)).ToListAsync();
        if (meters.Any(m => m.InstalledOn == installedOn))
        {
            state.AddModelError("Meter.InstalledOn", $"Another meter was installed on {installedOn:d}.");
            return;
        }

        // The meter as it would be (id 0 for a new one), among the others.
        var changed = new MeterInfo(meterId ?? 0, installedOn, start);
        meters.Add(changed);
        var readings = await db.Readings.Where(r => r.Meter!.Utility == utility)
            .Select(r => new { r.MeterId, r.Date, r.Value }).ToListAsync();

        var moved = readings.Where(r => ReadingCalculations.MeterFor(meters, r.Date)?.Id != r.MeterId).ToList();
        if (moved.Count > 0)
        {
            var own = moved.Where(r => r.MeterId == changed.Id).ToList();
            state.AddModelError("Meter.InstalledOn", own.Count > 0
                ? $"This meter has a reading before then ({own.Min(r => r.Date):d}): the date can't be after its first reading."
                : $"Another meter has a reading on or after then ({moved.Max(r => r.Date):d}): the date must be after its last reading.");
            return;
        }

        var first = readings.Where(r => r.MeterId == changed.Id).MinBy(r => r.Date);
        if (first is not null && first.Value < start)
            state.AddModelError("Meter.StartReading",
                $"That's higher than this meter's first reading ({UtilityUnits.FormatUnits(first.Value)} on {first.Date:d}).");
    }
}
