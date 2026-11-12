using GreenHedges.HomeServer.Utilities.Data;
using GreenHedges.HomeServer.Utilities.Models;

namespace GreenHedges.HomeServer.Utilities.Tests.Infrastructure;

/// <summary>Builds data for the context's user. Made-up readings only.</summary>
public static class TestData
{
    /// <summary>A meter for the context's user, in the utility's usual unit, with a reading for each date and value.</summary>
    public static async Task<Meter> AddMeterAsync(AppDbContext db, UtilityType utility = UtilityType.Electricity,
        DateOnly? installedOn = null, decimal? startReading = null, params (DateOnly Date, decimal Value)[] readings)
    {
        var meter = new Meter
        {
            Utility = utility,
            Unit = utility == UtilityType.Electricity ? MeterUnit.KilowattHours : MeterUnit.CubicMetres,
            InstalledOn = installedOn,
            StartReading = startReading,
            Readings = readings.Select(r => new Reading { Date = r.Date, Value = r.Value }).ToList()
        };
        db.Meters.Add(meter);
        await db.SaveChangesAsync();
        return meter;
    }
}
