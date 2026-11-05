using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// An insurance policy a vehicle has had. The latest one's end sets the vehicle's insurance renewal date. Policies can
/// overlap (e.g. a change of insurer mid-term). Pages use <c>InsurancePolicyView</c> / <c>InsurancePolicyInput</c>.
/// </summary>
public class InsurancePolicy : AuditedEntity
{
    public const int MaxProviderLength = 100;
    public const int MaxPolicyNumberLength = 50;
    public const int MaxNamedDriversLength = 500;
    public const int MaxNotesLength = 1000;

    public int Id { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    /// <summary>When cover starts and ends, in UK time as on the policy schedule (to the minute), not UTC.</summary>
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    [Required, StringLength(MaxProviderLength)]
    public string Provider { get; set; } = string.Empty;

    [Required, StringLength(MaxPolicyNumberLength)]
    public string PolicyNumber { get; set; } = string.Empty;

    public InsuranceCover Cover { get; set; } = InsuranceCover.Comprehensive;

    /// <summary>What the policy cost in all.</summary>
    public decimal Premium { get; set; }

    /// <summary>The total excess on a claim (compulsory plus voluntary).</summary>
    public decimal? Excess { get; set; }

    [StringLength(MaxNamedDriversLength)]
    public string? NamedDrivers { get; set; }

    [StringLength(MaxNotesLength)]
    public string? Notes { get; set; }
}
