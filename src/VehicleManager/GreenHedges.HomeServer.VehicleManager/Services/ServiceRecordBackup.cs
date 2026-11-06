namespace GreenHedges.HomeServer.VehicleManager.Services;

public sealed record ServiceRecordBackup : BackupRow
{
    public required int Id { get; init; }
    public required int VehicleId { get; init; }
    public required DateOnly Date { get; init; }
    public required int Mileage { get; init; }
    public required string Garage { get; set; }
    public string? GarageLocation { get; set; }
    public bool UnderPreviousOwner { get; init; }
    public bool MileageEstimated { get; init; }
}
