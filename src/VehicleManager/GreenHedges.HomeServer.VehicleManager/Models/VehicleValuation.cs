using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// A value for a vehicle, typed in from a valuation site (for an insurance quote's "current value"). Pages use
/// <c>VehicleValuationView</c> / <c>VehicleValuationInput</c> rather than this entity.
/// </summary>
public class VehicleValuation : AuditedEntity
{
    public int Id { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>The mileage the valuation was for.</summary>
    public int Mileage { get; set; }

    public decimal Value { get; set; }

    public ValuationType Type { get; set; }

    /// <summary>The condition the value is for, when the site gives several (e.g. trade poor, average and good).</summary>
    public ValuationGrade? Grade { get; set; }

    public ValuationSource Source { get; set; }
}
