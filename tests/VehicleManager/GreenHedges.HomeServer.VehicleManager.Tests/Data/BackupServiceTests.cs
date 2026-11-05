using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.Tests.Infrastructure;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Data;

public class BackupServiceTests : DatabaseTest
{
    /// <summary>A vehicle with due dates, a service interval, two services, two insurance policies and two MOT tests, a single and a split fill-up, and a brand with no stations.</summary>
    private async Task SeedAsync(AppDbContext db)
    {
        var vehicle = await TestData.AddVehicleAsync(db, "BACK 1");
        vehicle.FuelType = FuelType.Diesel;
        vehicle.MotDueDate = new DateOnly(2026, 11, 1);
        vehicle.TaxDueDate = new DateOnly(2027, 3, 1);
        var policyStart = new DateTime(2026, 1, 15, 14, 32, 0);
        db.InsurancePolicies.Add(new InsurancePolicy
        {
            OwnerId = vehicle.OwnerId, StartDate = policyStart, EndDate = new DateTime(2027, 1, 14, 23, 59, 0), Provider = "Acme Insurance",
            PolicyNumber = "POL-123", Cover = InsuranceCover.ThirdPartyFireAndTheft, Premium = 456.78m, Excess = 350m,
            NamedDrivers = "Me, Partner", Notes = "Protected no-claims bonus",
            // Put on mid-term, with a charge for it.
            Vehicles = [new() { Vehicle = vehicle, From = new DateTime(2026, 6, 1, 9, 15, 0), To = new DateTime(2027, 1, 14, 23, 59, 0), Adjustment = 25.50m }]
        });
        db.InsurancePolicies.Add(new InsurancePolicy
        {
            OwnerId = vehicle.OwnerId, StartDate = new DateTime(2025, 1, 15, 0, 0, 0), EndDate = policyStart, Provider = "Other Insurer",
            PolicyNumber = "OLD-1", Premium = 400m
        });
        vehicle.ServiceDueDate = new DateOnly(2027, 6, 30);
        vehicle.ServiceDueMileage = 45000;
        vehicle.ServiceIntervalMonths = 12;
        vehicle.ServiceIntervalMiles = 10000;
        vehicle.ServiceRecords.Add(new ServiceRecord { Date = new DateOnly(2025, 6, 30), Mileage = 35000, Garage = "Main Dealer", GarageLocation = "Leeds" });
        vehicle.ServiceRecords.Add(new ServiceRecord { Date = new DateOnly(2024, 6, 1), Mileage = 25000, Garage = "Local Garage", UnderPreviousOwner = true });
        vehicle.MotTests.Add(new MotTest
        {
            CompletedDate = new DateTime(2025, 11, 3, 10, 30, 0), Passed = true, ExpiryDate = new DateOnly(2026, 11, 2), Odometer = 38000,
            MotTestNumber = "123456789012", RegistrationAtTest = "BACK 1", DataSource = "DVSA",
            Defects = [new() { Sequence = 1, Type = "ADVISORY", Text = "Tyre worn" }, new() { Sequence = 2, Type = "MINOR", Text = "Wiper blade", Dangerous = true }]
        });
        vehicle.MotTests.Add(new MotTest { CompletedDate = new DateTime(2024, 11, 1, 9, 0, 0), Odometer = 72000, OdometerInKilometres = true });
        await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2026, 1, 1), ("Shell", "Meadowbank"));
        var split = await TestData.AddFillUpAsync(db, vehicle.Id, new DateOnly(2026, 2, 1), ("BP", "Oak Lane"), ("Shell", "Meadowbank"));
        split.Notes = "Split";
        db.FuelBrands.Add(new FuelBrand { Name = "Empty" });
        await db.SaveChangesAsync();
    }

    /// <summary>Everything the user has, without ids, so a restore can be compared with the original.</summary>
    private static async Task<List<string>> ContentsAsync(AppDbContext db) =>
    [
        .. await db.Owners.Select(o => $"owner {o.Name} user={o.UserId} {o.AddedBy} {o.AddedDate:O}").ToListAsync(),
        .. await db.Vehicles.Select(v => $"vehicle {v.RegistrationNumber} {v.Owner!.Name} {v.Make} {v.Model} {v.FuelType} user={v.UserId} {v.AddedDate:O} " +
                                            $"mot={v.MotDueDate} tax={v.TaxDueDate} service={v.ServiceDueDate}/{v.ServiceDueMileage} every {v.ServiceIntervalMonths}/{v.ServiceIntervalMiles}").ToListAsync(),
        .. await db.FuelBrands.Select(b => $"brand {b.Name} user={b.UserId} {b.AddedDate:O} {b.ModifiedDate:O}").ToListAsync(),
        .. await db.FuelStations.Select(s => $"station {s.FuelBrand!.Name}@{s.Name} user={s.UserId}").ToListAsync(),
        .. await db.FuelEntries.Select(f => $"fill-up {f.Vehicle!.RegistrationNumber} {f.Date} {f.Litres} {f.CostPerLitre} {f.Notes} {f.AddedBy}").ToListAsync(),
        .. await db.FuelEntryStops.Select(s => $"stop {s.FuelEntry!.Date} {s.Sequence} {s.FuelStation!.FuelBrand!.Name}@{s.FuelStation.Name}").ToListAsync(),
        .. await db.ServiceRecords.Select(s => $"service {s.Vehicle!.RegistrationNumber} {s.Date} {s.Mileage} {s.Garage} {s.GarageLocation} previous={s.UnderPreviousOwner} {s.AddedBy} {s.AddedDate:O}").ToListAsync(),
        .. await db.InsurancePolicyVehicles.Select(v => $"insured {v.Vehicle!.RegistrationNumber} on {v.Policy!.PolicyNumber} {v.From:O} {v.To:O} {v.Adjustment} {v.AddedDate:O}").ToListAsync(),
        .. await db.InsurancePolicies.Select(p => $"policy {p.Owner!.Name} {p.StartDate:O} {p.EndDate:O} {p.Provider} {p.PolicyNumber} {p.Cover} " +
                                                  $"{p.Premium} {p.Excess} {p.NamedDrivers} {p.Notes} {p.AddedBy} {p.AddedDate:O}").ToListAsync(),
        .. await db.MotTests.Select(t => $"mot {t.Vehicle!.RegistrationNumber} {t.CompletedDate:O} passed={t.Passed} {t.ExpiryDate} {t.Odometer} km={t.OdometerInKilometres} " +
                                         $"{t.MotTestNumber} {t.RegistrationAtTest} {t.DataSource} {t.AddedBy} {t.AddedDate:O}").ToListAsync(),
        .. await db.MotDefects.Select(d => $"defect {d.MotTest!.CompletedDate:O} {d.Sequence} {d.Type} {d.Text} dangerous={d.Dangerous} {d.AddedDate:O}").ToListAsync()
    ];

    private static List<string> Sorted(List<string> list) => [.. list.Order()];

    [DatabaseFact]
    public async Task Restoring_replaces_everything_with_the_backup()
    {
        BackupFile backup;
        List<string> original;
        await using (var db = Db())
        {
            await SeedAsync(db);
            original = Sorted(await ContentsAsync(db));
            backup = await new BackupService(db).CreateAsync();
        }
        Assert.Equal(new BackupCounts(1, 1, 3, 2, 2, 2), backup.Counts);

        // Change things after the backup.
        await using (var db = Db())
        {
            var later = await TestData.AddVehicleAsync(db, "LATER 1");
            await TestData.AddFillUpAsync(db, later.Id, new DateOnly(2026, 3, 1), ("Esso", "Hill Top"));
            (await db.FuelBrands.SingleAsync(b => b.Name == "Shell")).Name = "Shell Renamed";
            await db.SaveChangesAsync();
        }

        await using (var db = Db()) await new BackupService(db).RestoreAsync(backup);

        await using var check = Db();
        Assert.Equal(original, Sorted(await ContentsAsync(check)));   // including when rows were added and by whom
    }

    [DatabaseFact]
    public async Task A_backup_can_be_restored_into_another_account()
    {
        BackupFile backup;
        List<string> original;
        await using (var db = Db())
        {
            await SeedAsync(db);
            original = Sorted(await ContentsAsync(db));
            backup = await new BackupService(db).CreateAsync();
        }

        var other = NewUser();
        await using (var db = TestDatabase.As(other)) await new BackupService(db).RestoreAsync(backup);

        await using var check = TestDatabase.As(other);
        // The data is theirs now, but the audit columns still say who added it.
        Assert.Equal(original.Select(s => s.Replace($"user={UserId}", $"user={other}")).Order(), Sorted(await ContentsAsync(check)));
        await using var mine = Db();
        Assert.Equal(original, Sorted(await ContentsAsync(mine)));    // the first user's data is untouched
    }

    [DatabaseFact]
    public async Task Restoring_leaves_other_users_data_alone()
    {
        var other = NewUser();
        await using (var db = TestDatabase.As(other)) await SeedAsync(db);
        await using (var db = Db()) await SeedAsync(db);

        var empty = new BackupFile { Format = BackupFile.FormatName, Version = BackupFile.CurrentVersion, CreatedDate = DateTime.UtcNow };
        await using (var db = Db()) await new BackupService(db).RestoreAsync(empty);

        await using (var db = Db()) Assert.Equal(new BackupCounts(0, 0, 0, 0, 0, 0), await new BackupService(db).CountAsync());
        await using (var db = TestDatabase.As(other)) Assert.Equal(new BackupCounts(1, 1, 3, 2, 2, 2), await new BackupService(db).CountAsync());
    }

    [DatabaseFact]
    public async Task A_restore_that_fails_changes_nothing()
    {
        BackupFile backup;
        List<string> original;
        await using (var db = Db())
        {
            await SeedAsync(db);
            original = Sorted(await ContentsAsync(db));
            backup = await new BackupService(db).CreateAsync();
        }
        // Too big for the Litres column, which only the database checks.
        backup.FuelEntries[^1] = backup.FuelEntries[^1] with { Litres = 1_000_000m };

        await using (var db = Db())
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => new BackupService(db).RestoreAsync(backup));

        await using var check = Db();
        Assert.Equal(original, Sorted(await ContentsAsync(check)));
    }
}
