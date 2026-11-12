using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Utilities.Tests.Data;

/// <summary>What the database itself enforces, whatever the pages do.</summary>
public class SchemaTests : DatabaseTest
{
    private static readonly DateOnly Day = new(2026, 3, 1);

    [DatabaseFact]
    public async Task Readings_keep_three_decimal_places()
    {
        int meterId;
        await using (var db = Db())
            meterId = (await TestData.AddMeterAsync(db, UtilityType.Gas, Day, 0m, (Day.AddDays(30), 123.456m))).Id;

        await using var check = Db();
        Assert.Equal(123.456m, (await check.Readings.SingleAsync(r => r.MeterId == meterId)).Value);
    }

    [DatabaseFact]
    public async Task A_meter_has_one_reading_per_date()
    {
        await using var db = Db();
        var meter = await TestData.AddMeterAsync(db, readings: (Day, 100m));
        db.ChangeTracker.Clear();
        db.Readings.Add(new Reading { MeterId = meter.Id, Date = Day, Value = 101m });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [DatabaseFact]
    public async Task Two_meters_can_have_readings_on_the_same_date()
    {
        await using var db = Db();
        await TestData.AddMeterAsync(db, UtilityType.Electricity, readings: (Day, 100m));
        await TestData.AddMeterAsync(db, UtilityType.Gas, readings: (Day, 50m));

        Assert.Equal(2, await db.Readings.CountAsync());
    }

    [DatabaseFact]
    public async Task A_meter_with_readings_cant_be_deleted()
    {
        int meterId;
        await using (var db = Db()) meterId = (await TestData.AddMeterAsync(db, readings: (Day, 100m))).Id;

        await using var check = Db();
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => check.Meters.Where(m => m.Id == meterId).ExecuteDeleteAsync());
        Assert.True(await check.Readings.AnyAsync(r => r.MeterId == meterId));
    }

    [DatabaseFact]
    public async Task Utility_and_unit_are_stored_as_text()
    {
        int meterId;
        await using (var db = Db()) meterId = (await TestData.AddMeterAsync(db, UtilityType.Water)).Id;

        await using var check = Db();
        var stored = await check.Database
            .SqlQuery<string>($"SELECT Utility + '/' + Unit AS Value FROM Meters WHERE Id = {meterId}")
            .SingleAsync();
        Assert.Equal("Water/CubicMetres", stored);
    }
}
