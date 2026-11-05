using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class VehicleInput
{
    [Display(Name = "Owner")]
    public int OwnerId { get; set; }

    [Required]
    public VehicleType Type { get; set; } = VehicleType.Car;

    [Required]
    [Display(Name = "Fuel type")]
    public FuelType FuelType { get; set; } = FuelType.Unleaded;

    [Required, StringLength(50)]
    public string Make { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Model { get; set; } = string.Empty;

    [Required, StringLength(15)]
    [Display(Name = "Registration number")]
    public string RegistrationNumber { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Registration date")]
    public DateOnly? RegistrationDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Purchased date")]
    public DateOnly? PurchasedDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "MOT due")]
    public DateOnly? MotDueDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Tax due")]
    public DateOnly? TaxDueDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Service due")]
    public DateOnly? ServiceDueDate { get; set; }

    [Range(1, 9_999_999)]
    [Display(Name = "Service due mileage")]
    public int? ServiceDueMileage { get; set; }

    [Range(1, 120)]
    [Display(Name = "Service every (months)")]
    public int? ServiceIntervalMonths { get; set; }

    [Range(1, 100_000)]
    [Display(Name = "Service every (miles)")]
    public int? ServiceIntervalMiles { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public string DisplayName => $"{Make} {Model}".Trim();

    public static readonly Expression<Func<Vehicle, VehicleInput>> Projection = v => new VehicleInput
    {
        OwnerId = v.OwnerId,
        Type = v.Type,
        FuelType = v.FuelType,
        Make = v.Make,
        Model = v.Model,
        RegistrationNumber = v.RegistrationNumber,
        RegistrationDate = v.RegistrationDate,
        PurchasedDate = v.PurchasedDate,
        MotDueDate = v.MotDueDate,
        TaxDueDate = v.TaxDueDate,
        ServiceDueDate = v.ServiceDueDate,
        ServiceDueMileage = v.ServiceDueMileage,
        ServiceIntervalMonths = v.ServiceIntervalMonths,
        ServiceIntervalMiles = v.ServiceIntervalMiles,
        RowVersion = v.RowVersion
    };

    public void ApplyTo(Vehicle vehicle)
    {
        vehicle.OwnerId = OwnerId;
        vehicle.Type = Type;
        vehicle.FuelType = FuelType;
        vehicle.Make = Make;
        vehicle.Model = Model;
        vehicle.RegistrationNumber = RegistrationNumber;
        vehicle.RegistrationDate = RegistrationDate;
        vehicle.PurchasedDate = PurchasedDate;
        vehicle.MotDueDate = MotDueDate;
        vehicle.TaxDueDate = TaxDueDate;
        vehicle.ServiceDueDate = ServiceDueDate;
        vehicle.ServiceDueMileage = ServiceDueMileage;
        vehicle.ServiceIntervalMonths = ServiceIntervalMonths;
        vehicle.ServiceIntervalMiles = ServiceIntervalMiles;
    }
}
