using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>A vehicle's value, typed in from a valuation site (see <see cref="VehicleValuation"/>).</summary>
public sealed record VehicleValuationBackup : BackupRow
{
    public required int Id { get; init; }
    public required int VehicleId { get; init; }
    public required DateOnly Date { get; init; }
    public required int Mileage { get; init; }
    public required decimal Value { get; init; }
    public ValuationType Type { get; init; }
    public ValuationGrade? Grade { get; init; }
    public ValuationSource Source { get; init; }
}
