using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Services;

public sealed record VehicleBackup : BackupRow
{
    public required int Id { get; init; }
    public required int OwnerId { get; init; }
    public VehicleType Type { get; init; }
    public FuelType FuelType { get; init; } = FuelType.Unleaded;
    public required string Make { get; init; }
    public required string Model { get; init; }
    public required string RegistrationNumber { get; set; }
    public DateOnly? RegistrationDate { get; init; }
    public DateOnly? PurchasedDate { get; init; }
    public DateOnly? MotDueDate { get; init; }
    public DateOnly? TaxDueDate { get; init; }
    public DateOnly? ServiceDueDate { get; init; }
    public int? ServiceDueMileage { get; init; }
    public int? ServiceIntervalMonths { get; init; }
    public int? ServiceIntervalMiles { get; init; }
}
