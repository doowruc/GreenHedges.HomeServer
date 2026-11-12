using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Services;
using GreenHedges.HomeServer.Utilities.ViewModels;

namespace GreenHedges.HomeServer.Utilities.Pages.Readings;

/// <summary>What a reading must satisfy, adding or editing, beyond its annotations.</summary>
public static class ReadingRules
{
    /// <summary>
    /// Checks <paramref name="input"/> and finds the meter its date belongs to, adding errors to
    /// <paramref name="state"/>. A meter only counts up, so a reading can't be lower than the one before it on its
    /// meter (or the meter's start reading), nor higher than the one after.
    /// </summary>
    /// <returns>The meter's id; 0 when the utility has no meter yet (the caller adds its first); null with errors.</returns>
    public static async Task<int?> CheckAsync(AppDbContext db, UtilityType utility, ReadingInput input, int? readingId,
        DateOnly today, ModelStateDictionary state)
    {
        if (input.Value is not { } value) return null;
        if (decimal.Round(value, 3) != value)
            state.AddModelError("Reading.Value", "A reading has at most three decimal places.");
        if (input.Date > today)
            state.AddModelError("Reading.Date", "The date can't be in the future.");
        if (!state.IsValid) return null;

        var meters = await db.Meters.Where(m => m.Utility == utility)
            .Select(m => new MeterInfo(m.Id, m.InstalledOn, m.StartReading)).ToListAsync();
        if (meters.Count == 0) return 0;

        var meter = ReadingCalculations.MeterFor(meters, input.Date);
        if (meter is null)
        {
            state.AddModelError("Reading.Date", $"That's before the first meter was installed ({meters.Min(m => m.InstalledOn):d}).");
            return null;
        }

        var others = await db.Readings.Where(r => r.MeterId == meter.Id && r.Id != readingId)
            .Select(r => new { r.Date, r.Value }).ToListAsync();
        if (others.Any(r => r.Date == input.Date))
        {
            state.AddModelError("Reading.Date", $"There's already a reading for {input.Date:d}.");
            return null;
        }

        var before = others.Where(r => r.Date < input.Date).MaxBy(r => r.Date);
        var after = others.Where(r => r.Date > input.Date).MinBy(r => r.Date);
        if (before is not null && value < before.Value)
            state.AddModelError("Reading.Value",
                $"That's lower than the reading before it ({UtilityUnits.FormatUnits(before.Value)} on {before.Date:d}). If the meter was replaced, add the new meter first.");
        else if (before is null && meter.StartReading is { } start && value < start)
            state.AddModelError("Reading.Value", $"That's lower than the meter's start reading ({UtilityUnits.FormatUnits(start)}).");
        if (after is not null && value > after.Value)
            state.AddModelError("Reading.Value",
                $"That's higher than the reading after it ({UtilityUnits.FormatUnits(after.Value)} on {after.Date:d}).");

        return state.IsValid ? meter.Id : null;
    }
}
