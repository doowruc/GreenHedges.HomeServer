using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// An owner's insurance policy. The vehicles it covers, and when, are its <see cref="Vehicles"/> (a car can be switched
/// mid-term, and a policy can cover several). A vehicle's insurance renewal date is the end of the latest policy it's
/// still on when the policy ends. Policies can overlap. Pages use <c>InsurancePolicyView</c> / <c>InsurancePolicyInput</c>.
/// </summary>
public class InsurancePolicy : AuditedEntity
{
    public const int MaxProviderLength = 100;
    public const int MaxPolicyNumberLength = 50;
    public const int MaxNamedDriversLength = 500;
    public const int MaxNotesLength = 1000;
    public const int MaxNoClaimsYears = 50;

    public int Id { get; set; }

    public int OwnerId { get; set; }
    public Owner? Owner { get; set; }

    /// <summary>When cover starts and ends, in UK time as on the policy schedule (to the minute), not UTC.</summary>
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    [Required, StringLength(MaxProviderLength)]
    public string Provider { get; set; } = string.Empty;

    [Required, StringLength(MaxPolicyNumberLength)]
    public string PolicyNumber { get; set; } = string.Empty;

    public InsuranceCover Cover { get; set; } = InsuranceCover.Comprehensive;

    /// <summary>What the policy cost when taken out; mid-term changes add their <see cref="InsurancePolicyVehicle.Adjustment"/>.</summary>
    public decimal Premium { get; set; }

    /// <summary>The total excess on a claim (compulsory plus voluntary).</summary>
    public decimal? Excess { get; set; }

    /// <summary>Named drivers who aren't owners in the app (the owners are <see cref="Drivers"/>).</summary>
    [StringLength(MaxNamedDriversLength)]
    public string? NamedDrivers { get; set; }

    /// <summary>Years of no claims bonus the policy was priced on, and whether it's protected (a claim won't lose it).</summary>
    public int? NoClaimsYears { get; set; }
    public bool ProtectedNoClaims { get; set; }

    [StringLength(MaxNotesLength)]
    public string? Notes { get; set; }

    public List<InsurancePolicyVehicle> Vehicles { get; set; } = [];

    /// <summary>Owners who are named drivers on it (not the policyholder).</summary>
    public List<InsurancePolicyDriver> Drivers { get; set; } = [];
}
