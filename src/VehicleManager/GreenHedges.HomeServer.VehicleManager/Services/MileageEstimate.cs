using System.Globalization;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// An estimate of a vehicle's mileage on a date (for a service whose mileage wasn't recorded), from its odometer
/// readings: between the readings either side of it, in proportion to the days; or, outside them, moved on (or back)
/// from the nearest at the rate the miles were going up there. <see cref="Basis"/> says how, for the form to show.
/// </summary>
public sealed record MileageEstimate(int Mileage, string Basis)
{
    private static readonly CultureInfo Uk = new("en-GB");

    public static MileageEstimate? For(IEnumerable<MileageReading> readings, DateOnly date)
    {
        var sorted = readings.OrderBy(r => r.Date).ThenBy(r => r.Odometer).ToList();
        var before = sorted.LastOrDefault(r => r.Date <= date);
        var after = sorted.FirstOrDefault(r => r.Date >= date);

        if (before is not null && before.Date == date) return new(before.Odometer, $"The reading at {Describe(before)}.");
        if (before is not null && after is not null)
        {
            var share = (double)(date.DayNumber - before.Date.DayNumber) / (after.Date.DayNumber - before.Date.DayNumber);
            return new(Round(before.Odometer + share * (after.Odometer - before.Odometer)),
                $"Between {Describe(before)} and {Describe(after)}.");
        }
        if (before is not null)
        {
            var trend = MileageTrend.From(sorted.Select(r => (r.Date, r.Odometer)));
            return trend is null ? null : new(Round(before.Odometer + trend.MilesPerDay * (date.DayNumber - before.Date.DayNumber)),
                $"On from {Describe(before)}, at {Rate(trend.MilesPerDay)}.");
        }
        if (after is not null)
        {
            var perDay = EarlyMilesPerDay(sorted);
            return perDay is null ? null : new(Math.Max(0, Round(after.Odometer - perDay.Value * (after.Date.DayNumber - date.DayNumber))),
                $"Back from {Describe(after)}, at {Rate(perDay.Value)}.");
        }
        return null;
    }

    /// <summary>
    /// The miles per day over the earliest readings: as <see cref="MileageTrend"/> works out the latest rate, but from the
    /// first reading forwards.
    /// </summary>
    private static double? EarlyMilesPerDay(List<MileageReading> sorted)
    {
        var first = sorted[0];
        var until = first.Date.AddMonths(MileageTrend.RecentMonths);
        var last = sorted.Last(r => r.Date <= until);
        if (last.Date.DayNumber - first.Date.DayNumber < MileageTrend.MinDays) last = sorted[^1];
        var days = last.Date.DayNumber - first.Date.DayNumber;
        var miles = last.Odometer - first.Odometer;
        return days >= MileageTrend.MinDays && miles > 0 ? (double)miles / days : null;
    }

    private static int Round(double miles) => (int)Math.Round(miles, MidpointRounding.AwayFromZero);

    private static string Describe(MileageReading r) =>
        string.Create(Uk, $"{r.Odometer:N0} miles at {r.Source} on {r.Date:dd/MM/yyyy}");

    private static string Rate(double milesPerDay) =>
        string.Create(Uk, $"{milesPerDay * 365.25 / 12:N0} miles a month");

    /// <summary>
    /// The current user's odometer readings for a vehicle: its fill-ups', MOT tests' (in miles) and services' (but not
    /// <paramref name="exceptServiceId"/>, the one being edited), and none at its first registration.
    /// </summary>
    public static async Task<List<MileageReading>> ReadingsAsync(AppDbContext db, int vehicleId, int? exceptServiceId = null)
    {
        var readings = await db.FuelEntries.Where(f => f.VehicleId == vehicleId && f.Odometer != null)
            .Select(f => new MileageReading(f.Date, f.Odometer!.Value, "a fill-up")).ToListAsync();
        readings.AddRange((await db.MotTests.Where(t => t.VehicleId == vehicleId && t.Odometer != null)
                .Select(t => new { t.CompletedDate, Odometer = t.Odometer!.Value, t.OdometerInKilometres }).ToListAsync())
            .Select(t => new MileageReading(Reminders.UkDate(t.CompletedDate), MileageTrend.Miles(t.Odometer, t.OdometerInKilometres), "the MOT")));
        readings.AddRange(await db.ServiceRecords.Where(s => s.VehicleId == vehicleId && s.Id != exceptServiceId)
            .Select(s => new MileageReading(s.Date, s.Mileage, "a service")).ToListAsync());
        var registered = await db.Vehicles.Where(v => v.Id == vehicleId).Select(v => v.RegistrationDate).FirstOrDefaultAsync();
        if (registered is { } date) readings.Add(new(date, 0, "first registration"));
        return readings;
    }
}
