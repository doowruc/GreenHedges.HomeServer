using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

/// <summary>Builds data for the context's user.</summary>
public static class TestData
{
    public static async Task<Vehicle> AddVehicleAsync(AppDbContext db, string registration = "TEST 1")
    {
        // Owner names are unique per user, so later vehicles share the first one's owner.
        var owner = await db.Owners.FirstOrDefaultAsync(o => o.Name == "Test Owner") ?? new Owner { Name = "Test Owner" };
        var vehicle = new Vehicle { Owner = owner, Make = "Test", Model = "Car", RegistrationNumber = registration };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return vehicle;
    }

    /// <summary>
    /// A policy held by the vehicle's owner, covering it from 00:00 on <paramref name="start"/> to 23:59 on
    /// <paramref name="end"/> (as UK policies usually run). Add it to <c>db.InsurancePolicies</c>.
    /// </summary>
    public static InsurancePolicy Policy(Vehicle vehicle, DateOnly start, DateOnly end, string provider = "Test Insurer", decimal premium = 400m)
    {
        var (from, to) = (start.ToDateTime(TimeOnly.MinValue), end.ToDateTime(new TimeOnly(23, 59)));
        return new InsurancePolicy
        {
            OwnerId = vehicle.OwnerId, StartDate = from, EndDate = to, Provider = provider, PolicyNumber = "TEST-1", Premium = premium,
            Vehicles = [new InsurancePolicyVehicle { VehicleId = vehicle.Id, From = from, To = to }]
        };
    }

    /// <summary>Adds a fill-up at the given stops, e.g. <c>("Shell", "Meadowbank"), ("BP", "Oak Lane")</c>.</summary>
    public static async Task<FuelEntry> AddFillUpAsync(AppDbContext db, int vehicleId, DateOnly date, params (string Brand, string Station)[] stops)
    {
        var entry = new FuelEntry { VehicleId = vehicleId, Date = date, Litres = 40, CostPerLitre = 1.5m };
        await new FuelStops(db).SetStopsAsync(entry, stops.Select(s => new FuelStopInput { Brand = s.Brand, Station = s.Station }));
        db.FuelEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }
}
