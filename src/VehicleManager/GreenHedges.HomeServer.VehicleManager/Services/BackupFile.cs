using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Pages.Vehicles;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// A backup of one user's data: a JSON document with a list per table, written gzip-compressed
/// (<c>.json.gz</c>). Rows refer to each other by their ids in the file, which are replaced with new
/// ids on restore. The user id isn't stored, so a backup can be restored into any account.
/// </summary>
public sealed record BackupFile
{
    public const string FormatName = "vehicle-manager-backup";
    public const int CurrentVersion = 1;

    /// <summary>The most JSON <see cref="Read"/> accepts, after decompressing.</summary>
    public const int MaxJsonBytes = 100 * 1024 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new UtcDateTimeConverter() }
    };

    /// <summary>Always <see cref="FormatName"/>; required, so other JSON can't be mistaken for an empty backup.</summary>
    public required string Format { get; init; }
    public required int Version { get; init; }
    public required DateTime CreatedDate { get; init; }

    public List<OwnerBackup> Owners { get; init; } = [];
    public List<VehicleBackup> Vehicles { get; init; } = [];
    public List<FuelBrandBackup> FuelBrands { get; init; } = [];
    public List<FuelStationBackup> FuelStations { get; init; } = [];
    public List<FuelEntryBackup> FuelEntries { get; init; } = [];
    public List<FuelEntryStopBackup> FuelEntryStops { get; init; } = [];
    public List<ServiceRecordBackup> ServiceRecords { get; init; } = [];
    public List<VehicleValuationBackup> VehicleValuations { get; init; } = [];
    public List<InsurancePolicyBackup> InsurancePolicies { get; init; } = [];
    public List<InsurancePolicyVehicleBackup> InsurancePolicyVehicles { get; init; } = [];
    public List<ClaimBackup> Claims { get; init; } = [];
    public List<InsurancePolicyDriverBackup> InsurancePolicyDrivers { get; init; } = [];
    public List<MotTestBackup> MotTests { get; init; } = [];
    public List<MotDefectBackup> MotDefects { get; init; } = [];

    public BackupCounts Counts => new(Owners.Count, Vehicles.Count, FuelBrands.Count, FuelStations.Count, FuelEntries.Count, ServiceRecords.Count);

    /// <summary>The name to save the compressed backup as.</summary>
    public string FileName => $"vehicle-manager-backup-{CreatedDate:yyyy-MM-dd-HHmm}.json.gz";

    /// <summary>
    /// A SHA-256 hash of the data (ignoring when the backup was made), so two backups of unchanged
    /// data have the same hash. Unlike the audit dates, it also notices deletions.
    /// </summary>
    public string ContentHash() =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this with { CreatedDate = default }, JsonOptions)));

    /// <summary>The backup as gzip-compressed JSON.</summary>
    public async Task<byte[]> ToBytesAsync()
    {
        var file = new MemoryStream();
        await WriteAsync(file);
        return file.ToArray();
    }

    /// <summary>Writes the backup to <paramref name="output"/> as gzip-compressed JSON.</summary>
    public async Task WriteAsync(Stream output)
    {
        await using var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
        await JsonSerializer.SerializeAsync(gzip, this, JsonOptions);
    }

    /// <summary>
    /// Reads a backup, gzip-compressed or plain JSON (told apart by the gzip header, not the file
    /// name), and checks it can be restored: ids and links between rows, required values, lengths,
    /// and names that must be unique. Names and registrations are tidied as the app does.
    /// </summary>
    /// <exception cref="BackupFormatException">The file isn't a backup that can be restored.</exception>
    public static BackupFile Read(Stream input)
    {
        BackupFile? backup;
        try
        {
            var json = ReadJson(input);
            backup = JsonSerializer.Deserialize<BackupFile>(json, JsonOptions);
        }
        catch (InvalidDataException) { throw new BackupFormatException("The file is compressed, but isn't a valid gzip file."); }
        catch (JsonException ex) { throw new BackupFormatException($"The file isn't a valid backup: {ex.Message}"); }

        if (backup is null || backup.Format != FormatName)
            throw new BackupFormatException("The file isn't a Vehicle Manager backup.");
        if (backup.Version != CurrentVersion)
            throw new BackupFormatException($"The backup is version {backup.Version}; this app reads version {CurrentVersion}.");

        backup.Tidy();
        if (backup.Validate() is { Count: > 0 } errors)
            throw new BackupFormatException("The backup can't be restored: " + string.Join(" ", errors.Take(10)) +
                                            (errors.Count > 10 ? $" …and {errors.Count - 10} more." : ""));
        return backup;
    }

    private static ReadOnlySpan<byte> ReadJson(Stream input)
    {
        var buffer = new MemoryStream();
        CopyLimited(input, buffer);
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);

        if (bytes is [0x1F, 0x8B, ..])
        {
            var json = new MemoryStream();
            using (var gzip = new GZipStream(new MemoryStream(buffer.GetBuffer(), 0, (int)buffer.Length), CompressionMode.Decompress))
                CopyLimited(gzip, json);
            bytes = json.GetBuffer().AsSpan(0, (int)json.Length);
        }

        return bytes.StartsWith("﻿"u8) ? bytes[3..] : bytes;
    }

    /// <summary>Copies at most <see cref="MaxJsonBytes"/>, so a small compressed file can't expand without limit.</summary>
    private static void CopyLimited(Stream from, MemoryStream to)
    {
        var chunk = new byte[81920];
        int read;
        while ((read = from.Read(chunk)) > 0)
        {
            if (to.Length + read > MaxJsonBytes)
                throw new BackupFormatException($"The backup is too large (over {MaxJsonBytes / 1024 / 1024} MB of data).");
            to.Write(chunk, 0, read);
        }
    }

    private void Tidy()
    {
        foreach (var o in Owners)
        {
            o.Name = FuelStops.NormaliseName(o.Name);
            o.DriverNumber = OwnerInput.NormaliseDriverNumber(o.DriverNumber);
        }
        foreach (var v in Vehicles) v.RegistrationNumber = VehicleRules.NormaliseReg(v.RegistrationNumber);
        foreach (var b in FuelBrands) b.Name = FuelStops.NormaliseName(b.Name);
        foreach (var s in FuelStations) s.Name = FuelStops.NormaliseName(s.Name);
        foreach (var s in ServiceRecords)
        {
            s.Garage = FuelStops.NormaliseName(s.Garage);
            s.GarageLocation = string.IsNullOrWhiteSpace(s.GarageLocation) ? null : FuelStops.NormaliseName(s.GarageLocation);
        }
        foreach (var p in InsurancePolicies)
        {
            p.Provider = FuelStops.NormaliseName(p.Provider);
            p.PolicyNumber = p.PolicyNumber?.Trim() ?? string.Empty;
            p.NamedDrivers = string.IsNullOrWhiteSpace(p.NamedDrivers) ? null : FuelStops.NormaliseName(p.NamedDrivers);
        }
    }

    private List<string> Validate()
    {
        var errors = new List<string>();

        var ownerIds = UniqueIds(Owners, o => o.Id, "owner", errors);
        var vehicleIds = UniqueIds(Vehicles, v => v.Id, "vehicle", errors);
        var brandIds = UniqueIds(FuelBrands, b => b.Id, "fuel brand", errors);
        var stationIds = UniqueIds(FuelStations, s => s.Id, "fuel station", errors);
        var entryIds = UniqueIds(FuelEntries, f => f.Id, "fill-up", errors);

        foreach (var o in Owners)
        {
            Text(o.Name, 50, $"Owner {o.Id}'s name", errors);
            if (o.DriverNumber is { } number && !OwnerInput.DriverNumberPattern().IsMatch(number))
                errors.Add($"Owner {o.Id}'s driver number isn't a driver number.");
        }
        Unique(Owners, o => o.Name, o => $"There's more than one owner called \"{o.Name}\".", errors);

        foreach (var v in Vehicles)
        {
            if (!ownerIds.Contains(v.OwnerId)) errors.Add($"Vehicle {v.Id}'s owner ({v.OwnerId}) isn't in the backup.");
            if (!Enum.IsDefined(v.Type)) errors.Add($"Vehicle {v.Id}'s type ({v.Type}) isn't a vehicle type.");
            if (!Enum.IsDefined(v.FuelType)) errors.Add($"Vehicle {v.Id}'s fuel type ({v.FuelType}) isn't a fuel type.");
            Text(v.Make, 50, $"Vehicle {v.Id}'s make", errors);
            Text(v.Model, 50, $"Vehicle {v.Id}'s model", errors);
            Text(v.RegistrationNumber, 15, $"Vehicle {v.Id}'s registration", errors);
            if (v.ServiceDueMileage is <= 0) errors.Add($"Vehicle {v.Id}'s service due mileage ({v.ServiceDueMileage}) isn't a mileage.");
            if (v.ServiceIntervalMonths is <= 0 || v.ServiceIntervalMiles is <= 0) errors.Add($"Vehicle {v.Id}'s service interval isn't positive.");
            if (v.DisposalMethod is { } method && !Enum.IsDefined(method)) errors.Add($"Vehicle {v.Id}'s disposal ({method}) isn't a way of disposing of a vehicle.");
            if ((v.DisposalMethod is null) != (v.DisposedDate is null)) errors.Add($"Vehicle {v.Id} has a disposal method or date without the other.");
            if (v.PurchasePrice is < 0 or >= 10_000_000 || v.DisposalPrice is < 0 or >= 10_000_000) errors.Add($"Vehicle {v.Id}'s purchase or disposal price isn't a price.");
        }
        Unique(Vehicles, v => v.RegistrationNumber, v => $"There's more than one vehicle registered {v.RegistrationNumber}.", errors);

        foreach (var b in FuelBrands)
            Text(b.Name, FuelBrand.MaxNameLength, $"Fuel brand {b.Id}'s name", errors);
        Unique(FuelBrands, b => b.Name, b => $"There's more than one fuel brand called \"{b.Name}\".", errors);

        foreach (var s in FuelStations)
        {
            if (!brandIds.Contains(s.FuelBrandId)) errors.Add($"Fuel station {s.Id}'s brand ({s.FuelBrandId}) isn't in the backup.");
            Text(s.Name, FuelStation.MaxNameLength, $"Fuel station {s.Id}'s name", errors);
        }
        Unique(FuelStations, s => $"{s.FuelBrandId}|{s.Name}", s => $"Fuel brand {s.FuelBrandId} has more than one station called \"{s.Name}\".", errors);

        foreach (var f in FuelEntries)
        {
            if (!vehicleIds.Contains(f.VehicleId)) errors.Add($"Fill-up {f.Id}'s vehicle ({f.VehicleId}) isn't in the backup.");
            if (f.Notes is { Length: > 500 }) errors.Add($"Fill-up {f.Id}'s notes are longer than 500 characters.");
        }

        foreach (var s in FuelEntryStops)
        {
            if (!entryIds.Contains(s.FuelEntryId)) errors.Add($"A stop's fill-up ({s.FuelEntryId}) isn't in the backup.");
            if (!stationIds.Contains(s.FuelStationId)) errors.Add($"Fill-up {s.FuelEntryId}'s station ({s.FuelStationId}) isn't in the backup.");
        }
        Unique(FuelEntryStops, s => $"{s.FuelEntryId}|{s.Sequence}", s => $"Fill-up {s.FuelEntryId} has more than one stop {s.Sequence}.", errors);

        foreach (var s in ServiceRecords)
        {
            if (!vehicleIds.Contains(s.VehicleId)) errors.Add($"Service {s.Id}'s vehicle ({s.VehicleId}) isn't in the backup.");
            if (s.Mileage < 0) errors.Add($"Service {s.Id}'s mileage ({s.Mileage}) is negative.");
            Text(s.Garage, ServiceRecord.MaxGarageLength, $"Service {s.Id}'s garage", errors);
            if (s.GarageLocation is { Length: > ServiceRecord.MaxGarageLength })
                errors.Add($"Service {s.Id}'s garage location is longer than {ServiceRecord.MaxGarageLength} characters.");
        }
        UniqueIds(ServiceRecords, s => s.Id, "service", errors);

        UniqueIds(VehicleValuations, v => v.Id, "vehicle value", errors);
        foreach (var v in VehicleValuations)
        {
            if (!vehicleIds.Contains(v.VehicleId)) errors.Add($"Vehicle value {v.Id}'s vehicle ({v.VehicleId}) isn't in the backup.");
            if (v.Mileage < 0) errors.Add($"Vehicle value {v.Id}'s mileage ({v.Mileage}) is negative.");
            if (v.Value is <= 0 or >= 10_000_000) errors.Add($"Vehicle value {v.Id}'s value ({v.Value}) isn't a value.");
            if (!Enum.IsDefined(v.Type)) errors.Add($"Vehicle value {v.Id}'s type ({v.Type}) isn't a kind of value.");
            if (v.Grade is { } grade && !Enum.IsDefined(grade)) errors.Add($"Vehicle value {v.Id}'s grade ({grade}) isn't a grade.");
            if (!Enum.IsDefined(v.Source)) errors.Add($"Vehicle value {v.Id}'s source ({v.Source}) isn't a valuation site.");
        }

        var policies = new Dictionary<int, InsurancePolicyBackup>();
        foreach (var p in InsurancePolicies)
        {
            policies.TryAdd(p.Id, p);
            if (!ownerIds.Contains(p.OwnerId)) errors.Add($"Insurance policy {p.Id}'s owner ({p.OwnerId}) isn't in the backup.");
            if (p.EndDate <= p.StartDate) errors.Add($"Insurance policy {p.Id} doesn't end after it starts.");
            if (!Enum.IsDefined(p.Cover)) errors.Add($"Insurance policy {p.Id}'s cover ({p.Cover}) isn't a kind of cover.");
            if (p.Premium is <= 0 or >= 1_000_000) errors.Add($"Insurance policy {p.Id}'s premium ({p.Premium}) isn't a premium.");
            if (p.Excess is < 0 or >= 1_000_000) errors.Add($"Insurance policy {p.Id}'s excess ({p.Excess}) isn't an excess.");
            if (p.NoClaimsYears is < 0 or > InsurancePolicy.MaxNoClaimsYears)
                errors.Add($"Insurance policy {p.Id}'s no claims bonus ({p.NoClaimsYears} years) isn't 0 to {InsurancePolicy.MaxNoClaimsYears} years.");
            Text(p.Provider, InsurancePolicy.MaxProviderLength, $"Insurance policy {p.Id}'s insurer", errors);
            Text(p.PolicyNumber, InsurancePolicy.MaxPolicyNumberLength, $"Insurance policy {p.Id}'s policy number", errors);
            if (p.NamedDrivers is { Length: > InsurancePolicy.MaxNamedDriversLength })
                errors.Add($"Insurance policy {p.Id}'s named drivers are longer than {InsurancePolicy.MaxNamedDriversLength} characters.");
            if (p.Notes is { Length: > InsurancePolicy.MaxNotesLength })
                errors.Add($"Insurance policy {p.Id}'s notes are longer than {InsurancePolicy.MaxNotesLength} characters.");
        }
        UniqueIds(InsurancePolicies, p => p.Id, "insurance policy", errors);

        foreach (var v in InsurancePolicyVehicles)
        {
            if (!vehicleIds.Contains(v.VehicleId)) errors.Add($"Insurance policy {v.InsurancePolicyId}'s vehicle ({v.VehicleId}) isn't in the backup.");
            if (v.To <= v.From) errors.Add($"Insurance policy {v.InsurancePolicyId}'s cover for vehicle {v.VehicleId} doesn't end after it starts.");
            if (v.Adjustment is <= -1_000_000 or >= 1_000_000) errors.Add($"Insurance policy {v.InsurancePolicyId}'s adjustment ({v.Adjustment}) isn't an adjustment.");
            if (!policies.TryGetValue(v.InsurancePolicyId, out var policy))
                errors.Add($"A vehicle's insurance policy ({v.InsurancePolicyId}) isn't in the backup.");
            else if (v.From < policy.StartDate || v.To > policy.EndDate)
                errors.Add($"Insurance policy {v.InsurancePolicyId}'s cover for vehicle {v.VehicleId} isn't within the policy's dates.");
        }

        foreach (var d in InsurancePolicyDrivers)
        {
            if (!ownerIds.Contains(d.OwnerId)) errors.Add($"Insurance policy {d.InsurancePolicyId}'s named driver ({d.OwnerId}) isn't in the backup.");
            if (!policies.TryGetValue(d.InsurancePolicyId, out var policy)) errors.Add($"A named driver's insurance policy ({d.InsurancePolicyId}) isn't in the backup.");
            else if (policy.OwnerId == d.OwnerId) errors.Add($"Insurance policy {d.InsurancePolicyId}'s policyholder is also a named driver on it.");
        }
        Unique(InsurancePolicyDrivers, d => $"{d.InsurancePolicyId}/{d.OwnerId}", d => $"Insurance policy {d.InsurancePolicyId} names driver {d.OwnerId} more than once.", errors);

        UniqueIds(Claims, c => c.Id, "claim", errors);
        foreach (var c in Claims)
        {
            if (!ownerIds.Contains(c.OwnerId)) errors.Add($"Claim {c.Id}'s owner ({c.OwnerId}) isn't in the backup.");
            if (!vehicleIds.Contains(c.VehicleId)) errors.Add($"Claim {c.Id}'s vehicle ({c.VehicleId}) isn't in the backup.");
            if (c.InsurancePolicyId is { } policyId)
            {
                if (!policies.TryGetValue(policyId, out var policy)) errors.Add($"Claim {c.Id}'s insurance policy ({policyId}) isn't in the backup.");
                else if (policy.OwnerId != c.OwnerId) errors.Add($"Claim {c.Id}'s insurance policy ({policyId}) isn't its owner's.");
            }
            if (!Enum.IsDefined(c.Type)) errors.Add($"Claim {c.Id}'s type ({c.Type}) isn't a kind of claim.");
            if (!Enum.IsDefined(c.Fault)) errors.Add($"Claim {c.Id}'s fault ({c.Fault}) isn't who was at fault.");
            if (c.TotalValue is < 0 or >= 10_000_000) errors.Add($"Claim {c.Id}'s total value ({c.TotalValue}) isn't a value.");
        }

        var motTestIds = UniqueIds(MotTests, t => t.Id, "MOT test", errors);
        foreach (var t in MotTests)
        {
            if (!vehicleIds.Contains(t.VehicleId)) errors.Add($"MOT test {t.Id}'s vehicle ({t.VehicleId}) isn't in the backup.");
            if (t.Odometer < 0) errors.Add($"MOT test {t.Id}'s odometer reading ({t.Odometer}) is negative.");
            if (t.MotTestNumber is { Length: > MotTest.MaxTestNumberLength } || t.RegistrationAtTest is { Length: > 15 } ||
                t.DataSource is { Length: > MotTest.MaxDataSourceLength })
                errors.Add($"MOT test {t.Id} has a test number, registration or data source that's too long.");
        }

        foreach (var d in MotDefects)
        {
            if (!motTestIds.Contains(d.MotTestId)) errors.Add($"A defect's MOT test ({d.MotTestId}) isn't in the backup.");
            Text(d.Text, MotDefect.MaxTextLength, $"MOT test {d.MotTestId}'s defect {d.Sequence}", errors);
            if (d.Type is { Length: > MotDefect.MaxTypeLength }) errors.Add($"MOT test {d.MotTestId}'s defect {d.Sequence} has a type longer than {MotDefect.MaxTypeLength} characters.");
        }
        Unique(MotDefects, d => $"{d.MotTestId}|{d.Sequence}", d => $"MOT test {d.MotTestId} has more than one defect {d.Sequence}.", errors);

        foreach (var row in new IEnumerable<BackupRow>[] { Owners, Vehicles, FuelBrands, FuelStations, FuelEntries, FuelEntryStops, ServiceRecords, VehicleValuations, InsurancePolicies, InsurancePolicyVehicles, InsurancePolicyDrivers, Claims, MotTests, MotDefects }.SelectMany(r => r))
            if (row.AddedBy.Length > 128 || row.ModifiedBy.Length > 128)
                errors.Add("An AddedBy or ModifiedBy value is longer than 128 characters.");

        return errors.Distinct().ToList();
    }

    private static HashSet<int> UniqueIds<T>(List<T> rows, Func<T, int> id, string what, List<string> errors)
    {
        var ids = new HashSet<int>();
        foreach (var row in rows)
            if (!ids.Add(id(row))) errors.Add($"There's more than one {what} with id {id(row)}.");
        return ids;
    }

    /// <summary>Unique ignoring case, as the database's indexes are.</summary>
    private static void Unique<T>(List<T> rows, Func<T, string> key, Func<T, string> error, List<string> errors) =>
        errors.AddRange(rows.GroupBy(key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => error(g.First())));

    private static void Text(string? value, int maxLength, string what, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{what} is missing.");
        else if (value.Length > maxLength) errors.Add($"{what} is longer than {maxLength} characters.");
    }

    /// <summary>Times are UTC: written with a Z, and read back as UTC (a time without an offset is taken as UTC).</summary>
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetDateTime();
            return value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
