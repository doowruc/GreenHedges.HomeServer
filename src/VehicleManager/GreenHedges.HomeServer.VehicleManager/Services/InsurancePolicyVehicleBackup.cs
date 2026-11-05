using System.Text.Json.Serialization;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>A vehicle on an insurance policy, and when (see <see cref="InsurancePolicyVehicle"/>).</summary>
public sealed record InsurancePolicyVehicleBackup : BackupRow
{
    public required int InsurancePolicyId { get; init; }
    public required int VehicleId { get; init; }

    [JsonConverter(typeof(WallClockJsonConverter))]
    public required DateTime From { get; init; }

    [JsonConverter(typeof(WallClockJsonConverter))]
    public required DateTime To { get; init; }

    public decimal? Adjustment { get; init; }
}
