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
    public decimal? PurchasePrice { get; set; }

    // --- When and how it left (both or neither), and what it fetched: a sale price, part-exchange value, insurance
    //     payout or scrap value. From its disposal date it's left out of reminders, lookups and new records (see
    //     CurrentOn); its history stays. ---
    public DisposalMethod? DisposalMethod { get; set; }
    public DateOnly? DisposedDate { get; set; }
    public decimal? DisposalPrice { get; set; }

    /// <summary>Vehicles not yet disposed of on <paramref name="today"/> (one with a later disposal date still counts).</summary>
    public static System.Linq.Expressions.Expression<Func<Vehicle, bool>> CurrentOn(DateOnly today) =>
        v => v.DisposedDate == null || v.DisposedDate > today;

    // --- When things are next due. MOT and tax can be looked up (Services/VehicleLookup); service is typed in.
    //     Insurance renewal comes from the latest policy the vehicle is on at its end (InsurancePolicyVehicle). ---
    public DateOnly? MotDueDate { get; set; }
    public DateOnly? TaxDueDate { get; set; }
    public DateOnly? ServiceDueDate { get; set; }
    /// <summary>The odometer reading the next service is due at; its date is estimated from recent fill-ups (Services/MileageTrend).</summary>
    public int? ServiceDueMileage { get; set; }

    /// <summary>How often it's serviced. Adding its latest service sets the next one's due date and mileage from these.</summary>
    public int? ServiceIntervalMonths { get; set; }
    public int? ServiceIntervalMiles { get; set; }

    public List<FuelEntry> FuelEntries { get; set; } = [];
    public List<ServiceRecord> ServiceRecords { get; set; } = [];
    public List<MotTest> MotTests { get; set; } = [];
    public List<InsurancePolicyVehicle> InsurancePeriods { get; set; } = [];
}
