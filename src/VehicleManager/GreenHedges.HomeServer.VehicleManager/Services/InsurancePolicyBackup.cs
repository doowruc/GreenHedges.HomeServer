using System.Text.Json.Serialization;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>An owner's insurance policy (see <see cref="InsurancePolicy"/>); its vehicles are <see cref="InsurancePolicyVehicleBackup"/>s.</summary>
public sealed record InsurancePolicyBackup : BackupRow
{
    public required int Id { get; init; }
    public required int OwnerId { get; init; }

    [JsonConverter(typeof(WallClockJsonConverter))]
    public required DateTime StartDate { get; init; }

    [JsonConverter(typeof(WallClockJsonConverter))]
    public required DateTime EndDate { get; init; }

    public required string Provider { get; set; }
    public required string PolicyNumber { get; set; }
    public InsuranceCover Cover { get; init; }
    public required decimal Premium { get; init; }
    public decimal? Excess { get; init; }
    public string? NamedDrivers { get; set; }
    public int? NoClaimsYears { get; init; }
    public bool ProtectedNoClaims { get; init; }
    public string? Notes { get; init; }
}
