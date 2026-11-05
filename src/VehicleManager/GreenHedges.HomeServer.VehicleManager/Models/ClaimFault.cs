namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>Who was at fault in a claim: the owner (the policyholder), the other party, or both (split liability).</summary>
public enum ClaimFault
{
    Owner,
    OtherParty,
    Both
}
