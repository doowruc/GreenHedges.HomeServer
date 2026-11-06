using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class ServiceRecordInput
{
    [Required]
    [DataType(DataType.Date)]
    public DateOnly? Date { get; set; }

    [Required, Range(0, 9_999_999)]
    public int? Mileage { get; set; }

    [Required, StringLength(ServiceRecord.MaxGarageLength)]
    public string Garage { get; set; } = string.Empty;

    [StringLength(ServiceRecord.MaxGarageLength)]
    [Display(Name = "Garage location")]
    public string? GarageLocation { get; set; }

    [Display(Name = "Under a previous owner")]
    public bool UnderPreviousOwner { get; set; }

    [Display(Name = "Mileage estimated")]
    public bool MileageEstimated { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<ServiceRecord, ServiceRecordInput>> Projection = s => new ServiceRecordInput
    {
        Date = s.Date,
        Mileage = s.Mileage,
        Garage = s.Garage,
        GarageLocation = s.GarageLocation,
        UnderPreviousOwner = s.UnderPreviousOwner,
        MileageEstimated = s.MileageEstimated,
        RowVersion = s.RowVersion
    };

    /// <summary>Copies the fields across, tidying the names' spaces. Call only once the input is valid.</summary>
    public void ApplyTo(ServiceRecord service)
    {
        service.Date = Date!.Value;
        service.Mileage = Mileage!.Value;
        service.Garage = FuelStops.NormaliseName(Garage);
        service.GarageLocation = string.IsNullOrWhiteSpace(GarageLocation) ? null : FuelStops.NormaliseName(GarageLocation);
        service.UnderPreviousOwner = UnderPreviousOwner;
        service.MileageEstimated = MileageEstimated;
    }
}
