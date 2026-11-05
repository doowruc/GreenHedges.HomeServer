using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// A vehicle an <see cref="InsurancePolicy"/> covers, from <see cref="From"/> to <see cref="To"/> (within the policy's dates;
/// UK wall-clock times, like the policy's). Switching car mid-term ends one and starts another; a multi-car policy has
/// several at once.
/// </summary>
public class InsurancePolicyVehicle : AuditedEntity
{
    public int Id { get; set; }

    public int InsurancePolicyId { get; set; }
    public InsurancePolicy? Policy { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateTime From { get; set; }
    public DateTime To { get; set; }

    /// <summary>What the insurer charged (positive) or refunded (negative) to put this vehicle on the policy mid-term.</summary>
    public decimal? Adjustment { get; set; }
}
