namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>An owner who's a named driver on a policy (see <see cref="Models.InsurancePolicyDriver"/>).</summary>
public sealed record InsurancePolicyDriverBackup : BackupRow
{
    public required int InsurancePolicyId { get; init; }
    public required int OwnerId { get; init; }
}
