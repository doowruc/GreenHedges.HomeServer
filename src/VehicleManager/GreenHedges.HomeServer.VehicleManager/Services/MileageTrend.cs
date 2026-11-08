using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// How fast a vehicle is adding miles, from its odometer readings (its fill-ups' and its MOT tests'): the latest
/// reading, and the average miles per day since the earliest reading in the last <see cref="RecentMonths"/> months
/// (or, if those span less than <see cref="MinDays"/> days, since the earliest reading of all).
/// </summary>
public sealed record MileageTrend(DateOnly LatestDate, int LatestOdometer, double MilesPerDay)
{
    public const int RecentMonths = 6;
    public const int MinDays = 28;

    public double MilesPerMonth => MilesPerDay * 365.25 / 12;

    /// <summary>The estimated miles a year at this rate, to the nearest 100 (as insurance quotes ask for it).</summary>
    public int MilesPerYear => (int)(Math.Round(MilesPerDay * 365.25 / 100, MidpointRounding.AwayFromZero) * 100);

    /// <summary>When the odometer will reach (or reached) <paramref name="mileage"/>, at this rate.</summary>
    public DateOnly DateAt(int mileage) =>
        LatestDate.AddDays((int)Math.Clamp(Math.Round((mileage - LatestOdometer) / MilesPerDay), -36500, 36500));

    /// <returns>Null without two readings at least <see cref="MinDays"/> days apart, or if the miles didn't go up.</returns>
    public static MileageTrend? From(IEnumerable<(DateOnly Date, int Odometer)> readings)
    {
        var sorted = readings.OrderBy(r => r.Date).ThenBy(r => r.Odometer).ToList();
        if (sorted.Count < 2) return null;
        var latest = sorted[^1];
        var since = latest.Date.AddMonths(-RecentMonths);
        var earliest = sorted.First(r => r.Date >= since);
        if (latest.Date.DayNumber - earliest.Date.DayNumber < MinDays) earliest = sorted[0];

        var days = latest.Date.DayNumber - earliest.Date.DayNumber;
        var miles = latest.Odometer - earliest.Odometer;
        return days >= MinDays && miles > 0 ? new(latest.Date, latest.Odometer, (double)miles / days) : null;
    }

    /// <summary>The trend for each of the current user's vehicles that has one.</summary>
    public static async Task<Dictionary<int, MileageTrend>> ForVehiclesAsync(AppDbContext db)
    {
        var fillUps = (await db.FuelEntries.Where(f => f.Odometer != null)
                .Select(f => new { f.VehicleId, f.Date, Odometer = f.Odometer!.Value })
                .ToListAsync())
            .ToLookup(f => f.VehicleId, f => (f.Date, f.Odometer));
        var motTests = await MotReadingsAsync(db);
        return fillUps.Select(g => g.Key).Union(motTests.Select(g => g.Key))
            .Select(id => (Id: id, Trend: From(fillUps[id].Concat(motTests[id]))))
            .Where(t => t.Trend is not null)
            .ToDictionary(t => t.Id, t => t.Trend!);
    }

    /// <summary>The trend from fill-ups and MOT readings (<see cref="MotReadingsAsync"/>) already loaded.</summary>
    public static MileageTrend? From(IEnumerable<ViewModels.FuelEntryView> entries, IEnumerable<(DateOnly Date, int Odometer)> motReadings) =>
        From(entries.Where(e => e.Odometer is not null).Select(e => (e.Date, e.Odometer!.Value)).Concat(motReadings));

    /// <summary>The odometer readings of the current user's MOT tests, in miles, by vehicle id.</summary>
    public static async Task<ILookup<int, (DateOnly Date, int Odometer)>> MotReadingsAsync(AppDbContext db) =>
        (await db.MotTests.Where(t => t.Odometer != null)
            .Select(t => new { t.VehicleId, t.CompletedDate, Odometer = t.Odometer!.Value, t.OdometerInKilometres })
            .ToListAsync())
        .ToLookup(t => t.VehicleId, t => (Reminders.UkDate(t.CompletedDate), Miles(t.Odometer, t.OdometerInKilometres)));

    /// <summary>
    /// The mileage to value a vehicle at on <paramref name="date"/>: the latest reading moved on to that date at the
    /// trend's rate, rounded to the nearest 100; the latest reading itself without a trend (or for a date not after it);
    /// null without any readings.
    /// </summary>
    public static int? SuggestedMileage(IEnumerable<(DateOnly Date, int Odometer)> readings, DateOnly date)
    {
        var list = readings.ToList();
        if (list.Count == 0) return null;
        var trend = From(list);
        var latest = list.OrderBy(r => r.Date).ThenBy(r => r.Odometer).Last();
        if (trend is null || date <= trend.LatestDate) return latest.Odometer;
        var estimate = trend.LatestOdometer + trend.MilesPerDay * (date.DayNumber - trend.LatestDate.DayNumber);
        return (int)(Math.Round(estimate / 100, MidpointRounding.AwayFromZero) * 100);
    }

    /// <summary>The odometer readings (fill-ups' and MOT tests', in miles) of the current user's <paramref name="vehicleIds"/>.</summary>
    public static async Task<ILookup<int, (DateOnly Date, int Odometer)>> ReadingsAsync(AppDbContext db, IReadOnlyCollection<int> vehicleIds)
    {
        var fillUps = await db.FuelEntries.Where(f => vehicleIds.Contains(f.VehicleId) && f.Odometer != null)
            .Select(f => new { f.VehicleId, f.Date, Odometer = f.Odometer!.Value }).ToListAsync();
        var motTests = await db.MotTests.Where(t => vehicleIds.Contains(t.VehicleId) && t.Odometer != null)
            .Select(t => new { t.VehicleId, t.CompletedDate, Odometer = t.Odometer!.Value, t.OdometerInKilometres }).ToListAsync();
        return fillUps.Select(f => (f.VehicleId, Reading: (f.Date, f.Odometer)))
            .Concat(motTests.Select(t => (t.VehicleId, Reading: (Reminders.UkDate(t.CompletedDate), Miles(t.Odometer, t.OdometerInKilometres)))))
            .ToLookup(r => r.VehicleId, r => r.Reading);
    }

    /// <summary>An odometer reading in miles: one in kilometres is converted.</summary>
    public static int Miles(int odometer, bool inKilometres) =>
        inKilometres ? (int)Math.Round(odometer / 1.609344) : odometer;
}
