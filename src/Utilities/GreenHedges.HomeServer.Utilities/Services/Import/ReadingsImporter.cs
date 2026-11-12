using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Models;

namespace GreenHedges.HomeServer.Utilities.Services.Import;

/// <summary>What importing a utility's block of the sheet would do.</summary>
/// <param name="NewMeters">Meters to add, oldest first, with made-up negative ids: only for a utility with none yet.</param>
/// <param name="ToImport">The readings to add, oldest first, each with its meter's id (negative for a new meter).</param>
/// <param name="AlreadyThere">Readings on dates the utility already has: left alone.</param>
/// <param name="Skipped">Rows that can't be imported, with why (the sheet's unreadable rows included).</param>
/// <param name="Rows">The utility's readings as they would be, worked out, oldest first.</param>
/// <param name="Mismatches">Rows to import whose units a day here differ from the sheet's own.</param>
public sealed record UtilityPlan(
    UtilityType Utility,
    List<MeterInfo> NewMeters,
    List<(SheetReading Reading, int MeterId)> ToImport,
    int AlreadyThere,
    List<(int Row, string Reason)> Skipped,
    List<ReadingRow> Rows,
    List<int> Mismatches)
{
    /// <summary>The rows to import, worked out, oldest first (their ids are their sheet rows, negated).</summary>
    public IEnumerable<ReadingRow> NewRows => Rows.Where(r => r.Id < 0);
}

/// <summary>Plans the import of the meter readings sheet, and imports it. The planning is pure (<see cref="Plan"/>).</summary>
public class ReadingsImporter(AppDbContext db, TimeProvider time, ILogger<ReadingsImporter> logger)
{
    public async Task<List<UtilityPlan>> PlanAsync(WorkbookContents contents)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var meters = await db.Meters.Select(m => new { m.Utility, Info = new MeterInfo(m.Id, m.InstalledOn, m.StartReading) }).ToListAsync();
        var readings = await db.Readings.Select(r => new { r.Meter!.Utility, Info = new ReadingInfo(r.Id, r.MeterId, r.Date, r.Value) }).ToListAsync();

        return contents.Utilities.Select(sheet => Plan(sheet,
            meters.Where(m => m.Utility == sheet.Utility).Select(m => m.Info).ToList(),
            readings.Where(r => r.Utility == sheet.Utility).Select(r => r.Info).ToList(),
            today)).ToList();
    }

    /// <summary>
    /// A utility with no meters gets them from the sheet: its first, and a replacement wherever the reading falls,
    /// installed that day at 0 (as the sheet counted it). A utility with meters only gains readings on dates it doesn't
    /// have, each on the meter its date belongs to, where it fits between that meter's other readings.
    /// </summary>
    public static UtilityPlan Plan(SheetUtility sheet, IReadOnlyList<MeterInfo> existingMeters, IReadOnlyList<ReadingInfo> existingReadings,
        DateOnly today)
    {
        var skipped = sheet.Skipped.ToList();
        var newMeters = new List<MeterInfo>();
        var toImport = new List<(SheetReading Reading, int MeterId)>();
        var alreadyThere = 0;
        var existingDates = existingReadings.Select(r => r.Date).ToHashSet();
        var seen = new HashSet<DateOnly>();
        // Each meter's readings as they'd be: the existing ones and those planned so far.
        var onMeter = existingReadings.GroupBy(r => r.MeterId).ToDictionary(g => g.Key, g => g.Select(r => (r.Date, r.Value)).ToList());
        decimal? last = null;

        foreach (var reading in sheet.Readings.OrderBy(r => r.Date).ThenBy(r => r.Row))
        {
            if (reading.Date > today) { skipped.Add((reading.Row, "its date is in the future")); continue; }
            if (!seen.Add(reading.Date)) { skipped.Add((reading.Row, $"the sheet has an earlier row for {reading.Date:dd/MM/yyyy}")); continue; }
            if (existingDates.Contains(reading.Date)) { alreadyThere++; continue; }

            int meterId;
            if (existingMeters.Count == 0)
            {
                if (newMeters.Count == 0) newMeters.Add(new MeterInfo(-1, null, null));
                else if (reading.Value < last) newMeters.Add(new MeterInfo(-(newMeters.Count + 1), reading.Date, 0m));
                meterId = newMeters[^1].Id;
                last = reading.Value;
            }
            else
            {
                if (ReadingCalculations.MeterFor(existingMeters, reading.Date) is not { } meter)
                {
                    skipped.Add((reading.Row, "its date is before the first meter was installed"));
                    continue;
                }
                var others = onMeter.GetValueOrDefault(meter.Id) ?? [];
                var before = others.Where(r => r.Date < reading.Date).OrderBy(r => r.Date).Select(r => (decimal?)r.Value).LastOrDefault();
                var after = others.Where(r => r.Date > reading.Date).OrderBy(r => r.Date).Select(r => (decimal?)r.Value).FirstOrDefault();
                if (reading.Value < (before ?? meter.StartReading))
                {
                    skipped.Add((reading.Row, "its reading is lower than the one before it: if the meter was replaced, add the new meter first"));
                    continue;
                }
                if (reading.Value > after)
                {
                    skipped.Add((reading.Row, "its reading is higher than the one after it"));
                    continue;
                }
                meterId = meter.Id;
            }

            toImport.Add((reading, meterId));
            if (!onMeter.TryGetValue(meterId, out var list)) onMeter[meterId] = list = [];
            list.Add((reading.Date, reading.Value));
        }

        var rows = ReadingCalculations.Calculate([.. existingMeters, .. newMeters],
            [.. existingReadings, .. toImport.Select(t => new ReadingInfo(-t.Reading.Row, t.MeterId, t.Reading.Date, t.Reading.Value))]);
        var sheetPerDay = toImport.Where(t => t.Reading.SheetUnitsPerDay is not null).ToDictionary(t => -t.Reading.Row, t => t.Reading.SheetUnitsPerDay!.Value);
        var mismatches = rows
            .Where(r => r.UnitsPerDay is { } ours && sheetPerDay.TryGetValue(r.Id, out var theirs) && Math.Abs(ours - theirs) > 0.0005m * Math.Max(1, Math.Abs(theirs)))
            .Select(r => -r.Id).ToList();

        return new UtilityPlan(sheet.Utility, newMeters, toImport, alreadyThere, skipped.OrderBy(s => s.Row).ToList(), rows, mismatches);
    }

    /// <summary>Adds the plans' meters and readings, in one save.</summary>
    /// <returns>How many readings and meters were added.</returns>
    public async Task<(int Readings, int Meters)> ImportAsync(IReadOnlyCollection<UtilityPlan> plans)
    {
        foreach (var plan in plans)
        {
            var added = plan.NewMeters.ToDictionary(m => m.Id, m => new Meter
            {
                Utility = plan.Utility, Unit = plan.Utility.DefaultUnit(), InstalledOn = m.InstalledOn, StartReading = m.StartReading
            });
            db.Meters.AddRange(added.Values);
            foreach (var (reading, meterId) in plan.ToImport)
            {
                var entity = new Reading { Date = reading.Date, Value = reading.Value };
                if (added.TryGetValue(meterId, out var meter)) meter.Readings.Add(entity);
                else { entity.MeterId = meterId; db.Readings.Add(entity); }
            }
        }
        await db.SaveChangesAsync();

        var result = (Readings: plans.Sum(p => p.ToImport.Count), Meters: plans.Sum(p => p.NewMeters.Count));
        // Counts only: never the readings themselves.
        logger.LogInformation("Imported {Readings} readings and {Meters} meters from a spreadsheet", result.Readings, result.Meters);
        return result;
    }
}
