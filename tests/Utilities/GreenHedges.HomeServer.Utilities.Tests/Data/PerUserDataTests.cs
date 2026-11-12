using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Tests.Infrastructure;

namespace GreenHedges.HomeServer.Utilities.Tests.Data;

public class PerUserDataTests : DatabaseTest
{
    private static readonly DateOnly Day = new(2026, 3, 1);

    [DatabaseFact]
    public async Task New_meters_belong_to_the_signed_in_user()
    {
        await using var db = Db();
        var meter = await TestData.AddMeterAsync(db, readings: (Day, 100m));
        Assert.Equal(UserId, meter.UserId);
        Assert.Equal(UserId, meter.Readings[0].AddedBy);
    }

    [DatabaseFact]
    public async Task Users_only_see_their_own_data()
    {
        int meterId;
        await using (var db = Db()) meterId = (await TestData.AddMeterAsync(db, readings: (Day, 100m))).Id;

        await using var other = TestDatabase.As(NewUser());
        Assert.False(await other.Meters.AnyAsync());
        Assert.False(await other.Readings.AnyAsync());
        Assert.Equal(0, await other.Readings.Where(r => r.MeterId == meterId).ExecuteDeleteAsync());
        Assert.Equal(0, await other.Readings.ExecuteUpdateAsync(s => s.SetProperty(r => r.Value, 0m)));
    }

    [DatabaseFact]
    public async Task Nothing_is_visible_or_saved_without_a_signed_in_user()
    {
        await using (var db = Db()) await TestData.AddMeterAsync(db);
        await using var none = TestDatabase.As(null);
        Assert.False(await none.Meters.AnyAsync());
        none.Meters.Add(new Meter { Utility = UtilityType.Gas, Unit = MeterUnit.CubicMetres });
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.SaveChangesAsync());
    }
}
