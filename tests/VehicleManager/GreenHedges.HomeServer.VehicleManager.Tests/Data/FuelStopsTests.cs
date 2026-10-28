using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Data;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Data;

public class FuelStopsTests : DatabaseTest
{
    private async Task<int> SeedAsync()
    {
        await using var db = Db();
        var vehicle = await TestData.AddVehicleAsync(db);
        await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2026, 1, 1), ("Sainsbury's", "Hill Top"));
        await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2026, 1, 2), ("Asda", "Riverside"));
        await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2026, 1, 3), ("Morrisons", "Riverside"));
        return vehicle.Id;
    }

    private static async Task<ModelStateDictionary> ValidateAsync(AppDbContext db, List<FuelStopInput> rows)
    {
        var state = new ModelStateDictionary();
        await new FuelStops(db).ValidateAsync(rows, state, "Entry.Stops");
        return state;
    }

    [DatabaseFact]
    public async Task A_station_only_one_brand_has_gets_its_brand_filled_in()
    {
        await SeedAsync();
        await using var db = Db();
        List<FuelStopInput> rows = [new() { Station = " hill   top " }];
        var state = await ValidateAsync(db, rows);
        Assert.True(state.IsValid);
        Assert.Equal("Sainsbury's", rows[0].Brand);
        Assert.Equal("hill top", rows[0].Station);   // tidied
    }

    [DatabaseFact]
    public async Task A_station_several_brands_have_needs_the_brand()
    {
        await SeedAsync();
        await using var db = Db();
        var state = await ValidateAsync(db, [new() { Station = "Riverside" }]);
        Assert.Contains("More than one brand", state["Entry.Stops[0].Brand"]!.Errors.Single().ErrorMessage);
    }

    [DatabaseFact]
    public async Task Brand_and_station_are_both_needed_but_blank_rows_are_ignored()
    {
        await using var db = Db();
        var state = await ValidateAsync(db, [new() { Brand = "Shell" }, new() { Station = "Nowhere" }, new()]);
        Assert.Single(state["Entry.Stops[0].Station"]!.Errors);
        Assert.Contains("new station", state["Entry.Stops[1].Brand"]!.Errors.Single().ErrorMessage);
        Assert.Null(state["Entry.Stops[2].Brand"]);
    }

    [DatabaseFact]
    public async Task Existing_brands_and_stations_are_reused_and_new_ones_created()
    {
        var vehicleId = await SeedAsync();
        await using (var db = Db())
            await TestData.AddFillUpAsync(db, vehicleId, new DateOnly(2026, 1, 4), ("asda", "RIVERSIDE"), ("Newco", "Nowhere"));

        await using var check = Db();
        Assert.Equal(2, await check.FuelStations.CountAsync(s => s.Name == "Riverside"));   // "asda" + "RIVERSIDE" reused Asda's
        Assert.Equal(1, await check.FuelStations.CountAsync(s => s.Name == "Nowhere" && s.FuelBrand!.Name == "Newco"));
        var entry = await check.FuelEntries.Where(f => f.Date == new DateOnly(2026, 1, 4)).Select(FuelEntryView.Projection).SingleAsync();
        Assert.Equal("Asda + Newco", entry.Brand);
        Assert.Equal("Riverside + Nowhere", entry.Station);
    }

    [DatabaseFact]
    public async Task Changing_only_the_stops_still_goes_through_the_conflict_check()
    {
        var vehicleId = await SeedAsync();
        await using var db = Db();
        var entry = await db.FuelEntries.Include(f => f.Stops).SingleAsync(f => f.VehicleId == vehicleId && f.Date == new DateOnly(2026, 1, 1));
        var loaded = entry.RowVersion;

        await new FuelStops(db).SetStopsAsync(entry, [new() { Brand = "Asda", Station = "Riverside" }]);
        Assert.True(await db.TrySaveEditAsync(entry, loaded));
        Assert.NotEqual(loaded, entry.RowVersion);
        Assert.Single(entry.Stops);   // updated in place, not duplicated
    }
}
