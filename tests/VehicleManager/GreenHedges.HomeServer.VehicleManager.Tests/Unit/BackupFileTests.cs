using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class BackupFileTests
{
    private static readonly DateTime Added = new(2025, 3, 4, 10, 20, 30, DateTimeKind.Utc);

    private static BackupFile Sample(Func<BackupFile, BackupFile>? change = null)
    {
        var backup = new BackupFile
        {
            Format = BackupFile.FormatName,
            Version = BackupFile.CurrentVersion,
            CreatedDate = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc),
            Owners = [new() { Id = 1, Name = "Me", AddedDate = Added, AddedBy = "auth0|me" }],
            Vehicles = [new() { Id = 2, OwnerId = 1, Type = VehicleType.Van, FuelType = FuelType.Diesel, Make = "Ford", Model = "Transit", RegistrationNumber = "AB12 CDE" }],
            FuelBrands = [new() { Id = 3, Name = "Shell" }, new() { Id = 4, Name = "BP" }],
            FuelStations = [new() { Id = 5, FuelBrandId = 3, Name = "Meadowbank" }, new() { Id = 6, FuelBrandId = 4, Name = "Oak Lane" }],
            FuelEntries = [new() { Id = 7, VehicleId = 2, Date = new DateOnly(2026, 1, 2), Litres = 40.5m, CostPerLitre = 1.459m, Notes = "Split" }],
            FuelEntryStops = [new() { FuelEntryId = 7, Sequence = 1, FuelStationId = 5 }, new() { FuelEntryId = 7, Sequence = 2, FuelStationId = 6 }]
        };
        return change is null ? backup : change(backup);
    }

    private static string Json(BackupFile backup) => JsonSerializer.Serialize(backup, BackupFile.JsonOptions);

    private static BackupFile Read(string json) => BackupFile.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    private static string ReadError(string json) => Assert.Throws<BackupFormatException>(() => Read(json)).Message;

    private static string ReadError(BackupFile backup) => ReadError(Json(backup));

    [Fact]
    public async Task Reads_a_compressed_backup()
    {
        var file = new MemoryStream();
        await Sample().WriteAsync(file);
        Assert.Equal([0x1F, 0x8B], file.ToArray()[..2]);

        file.Position = 0;
        Assert.Equal(Json(Sample()), Json(BackupFile.Read(file)));
    }

    [Fact]
    public void Reads_plain_json_with_or_without_a_byte_order_mark()
    {
        Assert.Equal(Json(Sample()), Json(Read(Json(Sample()))));
        Assert.Equal(Json(Sample()), Json(Read("﻿" + Json(Sample()))));
    }

    [Fact]
    public void Times_are_written_as_utc_and_enums_as_names()
    {
        var json = Json(Sample());
        Assert.Contains("\"addedDate\": \"2025-03-04T10:20:30Z\"", json);
        Assert.Contains("\"type\": \"Van\"", json);
        Assert.Contains("\"fuelType\": \"Diesel\"", json);
        Assert.Contains("\"date\": \"2026-01-02\"", json);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]                                   // not even an empty backup: that would delete everything
    [InlineData("{\"owners\": []}")]
    [InlineData("{\"format\": \"something-else\", \"version\": 1, \"createdDate\": \"2026-01-01T00:00:00Z\"}")]
    public void Rejects_a_file_that_isnt_a_backup(string json) => ReadError(json);

    [Fact]
    public void Vehicles_in_a_backup_made_before_fuel_types_are_unleaded()
    {
        var json = Json(Sample()).Replace("\"fuelType\": \"Diesel\",", "");
        Assert.DoesNotContain("fuelType", json);
        Assert.Equal(FuelType.Unleaded, Read(json).Vehicles.Single().FuelType);
    }

    [Theory]
    [InlineData("\"Hydrogen\"")]
    [InlineData("5")]
    public void Rejects_an_unknown_fuel_type(string value) =>
        ReadError(Json(Sample()).Replace("\"fuelType\": \"Diesel\"", $"\"fuelType\": {value}"));

    [Fact]
    public void Rejects_a_service_due_mileage_that_isnt_a_mileage() =>
        Assert.Contains("Vehicle 2's service due mileage (0) isn't a mileage.",
            ReadError(Sample(b => b with { Vehicles = [b.Vehicles[0] with { ServiceDueMileage = 0 }] })));

    [Fact]
    public void Rejects_named_drivers_that_dont_link_up()
    {
        static InsurancePolicyDriverBackup Driver(int ownerId, int policyId = 8) => new() { InsurancePolicyId = policyId, OwnerId = ownerId };
        Assert.Contains("Insurance policy 8's named driver (99) isn't in the backup.",
            ReadError(Sample(b => b with { InsurancePolicies = [Policy()], InsurancePolicyDrivers = [Driver(99)] })));
        Assert.Contains("A named driver's insurance policy (99) isn't in the backup.", ReadError(Sample(b => b with { InsurancePolicyDrivers = [Driver(1, 99)] })));
        Assert.Contains("Insurance policy 8's policyholder is also a named driver on it.",
            ReadError(Sample(b => b with { InsurancePolicies = [Policy()], InsurancePolicyDrivers = [Driver(1)] })));
    }

    [Fact]
    public void Rejects_vehicle_values_that_dont_link_up_or_arent_values()
    {
        static VehicleValuationBackup Value(int vehicleId = 2) => new()
        {
            Id = 4, VehicleId = vehicleId, Date = new DateOnly(2026, 9, 1), Mileage = 41000, Value = 8250m,
            Type = ValuationType.Trade, Grade = ValuationGrade.Good, Source = ValuationSource.WhatCar
        };
        var value = Assert.Single(Read(Json(Sample(b => b with { VehicleValuations = [Value()] }))).VehicleValuations);
        Assert.Equal((8250m, ValuationType.Trade, ValuationGrade.Good, ValuationSource.WhatCar), (value.Value, value.Type, value.Grade!.Value, value.Source));

        Assert.Contains("Vehicle value 4's vehicle (99) isn't in the backup.", ReadError(Sample(b => b with { VehicleValuations = [Value(99)] })));
        Assert.Contains("Vehicle value 4's value (0) isn't a value.", ReadError(Sample(b => b with { VehicleValuations = [Value() with { Value = 0 }] })));
        Assert.Contains("Vehicle value 4's mileage (-1) is negative.", ReadError(Sample(b => b with { VehicleValuations = [Value() with { Mileage = -1 }] })));
        Assert.Contains("Vehicle value 4's grade (9) isn't a grade.", ReadError(Sample(b => b with { VehicleValuations = [Value() with { Grade = (ValuationGrade)9 }] })));
        Assert.Contains("vehicle value", ReadError(Sample(b => b with { VehicleValuations = [Value(), Value()] })));
    }

    [Fact]
    public void Rejects_claims_that_dont_link_up()
    {
        static ClaimBackup Claim(int ownerId = 1, int vehicleId = 2, int? policyId = 8) => new()
        {
            Id = 3, OwnerId = ownerId, VehicleId = vehicleId, InsurancePolicyId = policyId, Date = new DateOnly(2027, 3, 1),
            Fault = ClaimFault.Owner, TotalValue = 1200m
        };
        var claim = Assert.Single(Read(Json(Sample(b => b with { InsurancePolicies = [Policy()], Claims = [Claim()] }))).Claims);
        Assert.Equal((8, ClaimFault.Owner, 1200m), (claim.InsurancePolicyId!.Value, claim.Fault, claim.TotalValue));

        Assert.Contains("Claim 3's owner (99) isn't in the backup.", ReadError(Sample(b => b with { InsurancePolicies = [Policy()], Claims = [Claim(ownerId: 99)] })));
        Assert.Contains("Claim 3's vehicle (99) isn't in the backup.", ReadError(Sample(b => b with { InsurancePolicies = [Policy()], Claims = [Claim(vehicleId: 99)] })));
        Assert.Contains("Claim 3's insurance policy (99) isn't in the backup.", ReadError(Sample(b => b with { Claims = [Claim(policyId: 99)] })));
        Assert.Contains("Claim 3's total value (-1) isn't a value.",
            ReadError(Sample(b => b with { Claims = [Claim(policyId: null) with { TotalValue = -1 }] })));
    }

    [Fact]
    public void An_owners_driver_number_is_tidied_and_checked()
    {
        var owner = Assert.Single(Read(Json(Sample(b => b with { Owners = [b.Owners[0] with { DriverNumber = "testa 801015 ab9cd" }] }))).Owners);
        Assert.Equal("TESTA801015AB9CD", owner.DriverNumber);
        Assert.Contains("Owner 1's driver number isn't a driver number.",
            ReadError(Sample(b => b with { Owners = [b.Owners[0] with { DriverNumber = "ABC" }] })));
    }

    [Fact]
    public void Rejects_a_disposal_without_its_date_or_a_negative_price()
    {
        Assert.Contains("Vehicle 2 has a disposal method or date without the other.",
            ReadError(Sample(b => b with { Vehicles = [b.Vehicles[0] with { DisposalMethod = DisposalMethod.Sold }] })));
        Assert.Contains("Vehicle 2's purchase or disposal price isn't a price.",
            ReadError(Sample(b => b with { Vehicles = [b.Vehicles[0] with { PurchasePrice = -1 }] })));
    }

    [Fact]
    public void Rejects_a_service_for_a_vehicle_that_isnt_in_the_backup_or_without_a_garage()
    {
        var error = ReadError(Sample(b => b with
        {
            ServiceRecords = [new() { Id = 8, VehicleId = 99, Date = new DateOnly(2026, 1, 1), Mileage = 1000, Garage = " " }]
        }));
        Assert.Contains("Service 8's vehicle (99) isn't in the backup.", error);
        Assert.Contains("Service 8's garage is missing.", error);
    }

    [Fact]
    public void Reads_a_service_and_tidies_its_names()
    {
        var backup = Read(Json(Sample(b => b with
        {
            ServiceRecords = [new() { Id = 8, VehicleId = 2, Date = new DateOnly(2026, 1, 1), Mileage = 1000, Garage = " Main   Dealer ", GarageLocation = "  " }]
        })));
        var service = Assert.Single(backup.ServiceRecords);
        Assert.Equal(("Main Dealer", (string?)null), (service.Garage, service.GarageLocation));
        Assert.Equal(1, backup.Counts.Services);
    }

    [Fact]
    public void Reads_MOT_tests_with_their_defects()
    {
        var backup = Read(Json(Sample(b => b with
        {
            MotTests = [new() { Id = 9, VehicleId = 2, CompletedDate = Added, Passed = true, Odometer = 30000, MotTestNumber = "123456789012" }],
            MotDefects = [new() { MotTestId = 9, Sequence = 1, Type = "ADVISORY", Text = "Tyre worn" }]
        })));
        Assert.Equal((9, "Tyre worn"), (Assert.Single(backup.MotTests).Id, Assert.Single(backup.MotDefects).Text));
    }

    [Fact]
    public void Rejects_MOT_tests_and_defects_that_dont_link_up()
    {
        var error = ReadError(Sample(b => b with
        {
            MotTests = [new() { Id = 9, VehicleId = 99, CompletedDate = Added, Odometer = -1 }],
            MotDefects = [new() { MotTestId = 98, Sequence = 1, Text = "Tyre worn" }, new() { MotTestId = 9, Sequence = 1, Text = " " },
                          new() { MotTestId = 9, Sequence = 1, Text = "Again" }]
        }));
        Assert.Contains("MOT test 9's vehicle (99) isn't in the backup.", error);
        Assert.Contains("MOT test 9's odometer reading (-1) is negative.", error);
        Assert.Contains("A defect's MOT test (98) isn't in the backup.", error);
        Assert.Contains("MOT test 9's defect 1 is missing.", error);
        Assert.Contains("MOT test 9 has more than one defect 1.", error);
    }

    private static InsurancePolicyBackup Policy(int ownerId = 1) => new()
    {
        Id = 8, OwnerId = ownerId, StartDate = new DateTime(2026, 11, 5, 14, 32, 0), EndDate = new DateTime(2027, 11, 4, 23, 59, 0),
        Provider = " Acme   Insurance ", PolicyNumber = " POL-1 ", Cover = InsuranceCover.ThirdPartyOnly, Premium = 456.78m, Excess = 250m,
        NamedDrivers = " Me,  Partner ", NoClaimsYears = 9, ProtectedNoClaims = true, Notes = "Renewed by phone"
    };

    /// <summary>Vehicle 2 switched on to policy 8 mid-term, with a refund.</summary>
    private static InsurancePolicyVehicleBackup Covered() => new()
    {
        InsurancePolicyId = 8, VehicleId = 2, From = new DateTime(2027, 2, 1, 9, 15, 0), To = new DateTime(2027, 11, 4, 23, 59, 0), Adjustment = -12.5m
    };

    [Fact]
    public void Insurance_policies_keep_their_uk_times_without_an_offset_and_are_tidied()
    {
        var json = Json(Sample(b => b with { InsurancePolicies = [Policy()], InsurancePolicyVehicles = [Covered()] }));
        Assert.Contains("\"startDate\": \"2026-11-05T14:32\"", json);
        Assert.Contains("\"endDate\": \"2027-11-04T23:59\"", json);
        Assert.Contains("\"cover\": \"ThirdPartyOnly\"", json);

        var policy = Assert.Single(Read(json).InsurancePolicies);
        Assert.Equal((new DateTime(2026, 11, 5, 14, 32, 0), new DateTime(2027, 11, 4, 23, 59, 0)), (policy.StartDate, policy.EndDate));
        Assert.Equal(DateTimeKind.Unspecified, policy.StartDate.Kind);
        Assert.Equal(("Acme Insurance", "POL-1", "Me, Partner", 456.78m), (policy.Provider, policy.PolicyNumber, policy.NamedDrivers, policy.Premium));
        Assert.Equal(((int?)9, true), (policy.NoClaimsYears, policy.ProtectedNoClaims));
        Assert.Contains("\"from\": \"2027-02-01T09:15\"", json);
        var covered = Assert.Single(Read(json).InsurancePolicyVehicles);
        Assert.Equal((8, 2, new DateTime(2027, 2, 1, 9, 15, 0), (decimal?)-12.5m), (covered.InsurancePolicyId, covered.VehicleId, covered.From, covered.Adjustment));
    }

    [Fact]
    public void A_vehicles_insurance_renewal_date_from_an_older_backup_is_ignored()
    {
        var json = Json(Sample()).Replace("\"make\":", "\"insuranceDueDate\": \"2027-01-15\", \"make\":");
        Assert.Contains("insuranceDueDate", json);
        Assert.Single(Read(json).Vehicles);
    }

    [Fact]
    public void Rejects_insurance_policies_that_dont_link_up_or_add_up()
    {
        Assert.Contains("Insurance policy 8's owner (99) isn't in the backup.", ReadError(Sample(b => b with { InsurancePolicies = [Policy(99)] })));
        Assert.Contains("Insurance policy 8's vehicle (99) isn't in the backup.",
            ReadError(Sample(b => b with { InsurancePolicies = [Policy()], InsurancePolicyVehicles = [Covered() with { VehicleId = 99 }] })));
        Assert.Contains("A vehicle's insurance policy (99) isn't in the backup.",
            ReadError(Sample(b => b with { InsurancePolicyVehicles = [Covered() with { InsurancePolicyId = 99 }] })));
        Assert.Contains("Insurance policy 8's cover for vehicle 2 isn't within the policy's dates.",
            ReadError(Sample(b => b with { InsurancePolicies = [Policy()], InsurancePolicyVehicles = [Covered() with { To = new DateTime(2028, 1, 1) }] })));
        Assert.Contains("Insurance policy 8 doesn't end after it starts.",
            ReadError(Sample(b => b with { InsurancePolicies = [Policy() with { EndDate = new DateTime(2026, 11, 5, 14, 32, 0) }] })));
        Assert.Contains("Insurance policy 8's no claims bonus (51 years) isn't 0 to 50 years.",
            ReadError(Sample(b => b with { InsurancePolicies = [Policy() with { NoClaimsYears = 51 }] })));
        Assert.Contains("Insurance policy 8's premium (0) isn't a premium.", ReadError(Sample(b => b with { InsurancePolicies = [Policy() with { Premium = 0 }] })));
        Assert.Contains("Insurance policy 8's policy number is missing.", ReadError(Sample(b => b with { InsurancePolicies = [Policy() with { PolicyNumber = " " }] })));
        Assert.Contains("Insurance policy 8's insurer is missing.", ReadError(Sample(b => b with { InsurancePolicies = [Policy() with { Provider = "" }] })));
    }

    [Fact]
    public void Rejects_a_newer_version() =>
        Assert.Contains("version 2", ReadError(Sample(b => b with { Version = 2 })));

    [Fact]
    public void Rejects_a_corrupt_compressed_file() =>
        Assert.Throws<BackupFormatException>(() => BackupFile.Read(new MemoryStream([0x1F, 0x8B, 1, 2, 3, 4])));

    [Fact]
    public void Rejects_a_compressed_file_that_expands_too_far()
    {
        var file = new MemoryStream();
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(new byte[BackupFile.MaxJsonBytes + 1]);
        file.Position = 0;
        Assert.Contains("too large", Assert.Throws<BackupFormatException>(() => BackupFile.Read(file)).Message);
    }

    [Fact]
    public void Rejects_links_to_rows_that_arent_in_the_backup()
    {
        var error = ReadError(Sample(b =>
        {
            b.Vehicles[0] = b.Vehicles[0] with { OwnerId = 99 };
            b.FuelStations[0] = b.FuelStations[0] with { FuelBrandId = 98 };
            b.FuelEntryStops[0] = b.FuelEntryStops[0] with { FuelStationId = 97 };
            return b;
        }));
        Assert.Contains("owner (99)", error);
        Assert.Contains("brand (98)", error);
        Assert.Contains("station (97)", error);
    }

    [Fact]
    public void Rejects_names_the_database_would_refuse()
    {
        var error = ReadError(Sample(b =>
        {
            b.FuelBrands.Add(new() { Id = 10, Name = "shell" });
            b.Vehicles.Add(b.Vehicles[0] with { Id = 11, RegistrationNumber = "ab12  cde" });
            b.Owners.Add(new() { Id = 12, Name = new string('x', 51) });
            b.FuelStations.Add(new() { Id = 13, FuelBrandId = 4, Name = " " });
            b.FuelEntryStops.Add(new() { FuelEntryId = 7, Sequence = 2, FuelStationId = 5 });
            return b;
        }));
        Assert.Contains("more than one fuel brand called \"Shell\"", error);
        Assert.Contains("more than one vehicle registered AB12 CDE", error);
        Assert.Contains("longer than 50 characters", error);
        Assert.Contains("Fuel station 13's name is missing", error);
        Assert.Contains("more than one stop 2", error);
    }

    [Fact]
    public void Rejects_a_row_missing_a_required_value() =>
        ReadError(Json(Sample()).Replace("\"make\": \"Ford\",", ""));

    [Fact]
    public void Tidies_registrations_and_names_like_the_app()
    {
        var backup = Read(Json(Sample(b =>
        {
            b.Vehicles[0].RegistrationNumber = " ab12   cde ";
            b.FuelBrands[0].Name = " Shell  Fuel ";
            return b;
        })));
        Assert.Equal("AB12 CDE", backup.Vehicles[0].RegistrationNumber);
        Assert.Equal("Shell Fuel", backup.FuelBrands[0].Name);
    }

    [Fact]
    public void The_content_hash_ignores_when_the_backup_was_made_but_not_the_data()
    {
        var hash = Sample().ContentHash();
        Assert.Matches("^[0-9A-F]{64}$", hash);
        Assert.Equal(hash, Sample(b => b with { CreatedDate = DateTime.UtcNow }).ContentHash());
        Assert.NotEqual(hash, Sample(b => { b.FuelEntryStops.RemoveAt(1); return b; }).ContentHash());
        Assert.NotEqual(hash, Sample(b => { b.FuelEntries[0] = b.FuelEntries[0] with { Litres = 40.6m }; return b; }).ContentHash());
    }

    [Fact]
    public void File_name_has_when_the_backup_was_made() =>
        Assert.Equal("vehicle-manager-backup-2026-10-07-0900.json.gz", Sample().FileName);
}
