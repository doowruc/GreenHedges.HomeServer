namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>How a vehicle left: see <see cref="Vehicle.DisposalMethod"/>.</summary>
public enum DisposalMethod
{
    Sold,
    PartExchanged,
    WrittenOff,
    Scrapped
}
