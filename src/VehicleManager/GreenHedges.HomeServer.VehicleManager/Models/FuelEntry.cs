using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>A stored fill-up. Pages use <c>FuelEntryView</c> / <c>FuelEntryInput</c> rather than this entity.</summary>
public class FuelEntry : AuditedEntity
{
    public int Id { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateOnly Date { get; set; }

    public decimal Litres { get; set; }
    public decimal CostPerLitre { get; set; }
    public decimal? VoucherPerLitre { get; set; }

    // --- Trip computer figures (as shown on the dash at fill-up) ---
    public decimal? TripAverageMpg { get; set; }
    public decimal? TripMiles { get; set; }
    public int? Range { get; set; }
    public int? Odometer { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    /// <summary>The brand(s) and station(s) the fuel came from, in order.</summary>
    public List<FuelEntryStop> Stops { get; set; } = [];
}
