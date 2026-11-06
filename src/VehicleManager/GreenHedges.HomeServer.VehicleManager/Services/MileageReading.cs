namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>An odometer reading and where it came from, e.g. "the MOT" (for <see cref="MileageEstimate"/>'s basis).</summary>
public sealed record MileageReading(DateOnly Date, int Odometer, string Source);
