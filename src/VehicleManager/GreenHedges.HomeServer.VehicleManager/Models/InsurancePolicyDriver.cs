using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// An owner who's a named driver on someone else's policy, so a renewal quote (pre-renewal) includes their claims and
/// licence. Drivers who aren't owners in the app stay in the policy's free-text <see cref="InsurancePolicy.NamedDrivers"/>.
/// Goes with the policy or the owner.
/// </summary>
public class InsurancePolicyDriver : AuditedEntity
{
    public int Id { get; set; }

    public int InsurancePolicyId { get; set; }
    public InsurancePolicy? Policy { get; set; }

    public int OwnerId { get; set; }
    public Owner? Owner { get; set; }
}
