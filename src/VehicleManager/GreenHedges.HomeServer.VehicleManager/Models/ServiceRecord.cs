using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>A service a vehicle has had. Pages use <c>ServiceRecordView</c> / <c>ServiceRecordInput</c> rather than this entity.</summary>
public class ServiceRecord : AuditedEntity
{
    public const int MaxGarageLength = 100;

    public int Id { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>The odometer reading at the service.</summary>
    public int Mileage { get; set; }

    [Required, StringLength(MaxGarageLength)]
    public string Garage { get; set; } = string.Empty;

    [StringLength(MaxGarageLength)]
    public string? GarageLocation { get; set; }

    /// <summary>Done while a previous owner had the vehicle, rather than the user.</summary>
    public bool UnderPreviousOwner { get; set; }
}
