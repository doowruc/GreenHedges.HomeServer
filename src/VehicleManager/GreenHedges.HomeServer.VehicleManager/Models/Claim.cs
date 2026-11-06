using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// An insurance claim made by an owner: for one of the user's vehicles, optionally on one of the owner's policies (an
/// older claim may predate the policies recorded). Deleting the vehicle deletes it; deleting the policy unlinks it; an
/// owner with claims can't be deleted. Pages use <c>ClaimView</c> / <c>ClaimInput</c>.
/// </summary>
public class Claim : AuditedEntity
{
    public int Id { get; set; }

    public int OwnerId { get; set; }
    public Owner? Owner { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public int? InsurancePolicyId { get; set; }
    public InsurancePolicy? InsurancePolicy { get; set; }

    /// <summary>When the incident happened.</summary>
    public DateOnly Date { get; set; }

    public ClaimType Type { get; set; } = ClaimType.Accident;
    public ClaimFault Fault { get; set; }

    /// <summary>What the claim came to in all (repairs, the other party's costs and so on).</summary>
    public decimal TotalValue { get; set; }
}
