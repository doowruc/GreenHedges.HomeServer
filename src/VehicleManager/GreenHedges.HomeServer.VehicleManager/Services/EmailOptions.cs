namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>The address reminder emails come from (<c>Resend:From</c>); its domain must be verified in Resend.</summary>
public sealed record EmailOptions(string From);
