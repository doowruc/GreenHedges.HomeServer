using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>An owner's insurance claim (see <see cref="Claim"/>).</summary>
public sealed record ClaimBackup : BackupRow
{
    public required int Id { get; init; }
    public required int OwnerId { get; init; }
    public required int VehicleId { get; init; }
    public int? InsurancePolicyId { get; init; }
    public required DateOnly Date { get; init; }
    public ClaimType Type { get; init; }
    public ClaimFault Fault { get; init; }
    public required decimal TotalValue { get; init; }
}
