using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>A stored vehicle. Pages use <c>VehicleView</c> / <c>VehicleInput</c> rather than this entity.</summary>
public class Vehicle : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    public int OwnerId { get; set; }
    public Owner? Owner { get; set; }

    public VehicleType Type { get; set; } = VehicleType.Car;
    public FuelType FuelType { get; set; } = FuelType.Unleaded;

    [Required, StringLength(50)]
    public string Make { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Model { get; set; } = string.Empty;

    [Required, StringLength(15)]
    public string RegistrationNumber { get; set; } = string.Empty;

    public DateOnly? RegistrationDate { get; set; }
    public DateOnly? PurchasedDate { get; set; }

    // --- When things are next due. MOT and tax can be looked up (Services/VehicleLookup); insurance and service are typed in. ---
    public DateOnly? MotDueDate { get; set; }
    public DateOnly? TaxDueDate { get; set; }
    public DateOnly? InsuranceDueDate { get; set; }
    public DateOnly? ServiceDueDate { get; set; }
    /// <summary>The odometer reading the next service is due at; its date is estimated from recent fill-ups (Services/MileageTrend).</summary>
    public int? ServiceDueMileage { get; set; }

    /// <summary>How often it's serviced. Adding its latest service sets the next one's due date and mileage from these.</summary>
    public int? ServiceIntervalMonths { get; set; }
    public int? ServiceIntervalMiles { get; set; }

    public List<FuelEntry> FuelEntries { get; set; } = [];
    public List<ServiceRecord> ServiceRecords { get; set; } = [];
    public List<MotTest> MotTests { get; set; } = [];
}
