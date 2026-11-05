namespace GreenHedges.HomeServer.VehicleManager.Services;

public sealed record OwnerBackup : BackupRow
{
    public required int Id { get; init; }
    public required string Name { get; set; }
    public DateOnly? LicenceIssueDate { get; init; }
    public string? DriverNumber { get; set; }
}
