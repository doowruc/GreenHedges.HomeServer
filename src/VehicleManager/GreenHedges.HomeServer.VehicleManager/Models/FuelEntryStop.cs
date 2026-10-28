using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// Where a fill-up's fuel came from: a station (and through it, a brand). Most fill-ups have one
/// stop; a "split" fill-up (topped up at two stations) has one per station, numbered by <see cref="Sequence"/>.
/// </summary>
public class FuelEntryStop : AuditedEntity
{
    public int Id { get; set; }

    public int FuelEntryId { get; set; }
    public FuelEntry? FuelEntry { get; set; }

    /// <summary>1 for the first stop, 2 for the second, and so on.</summary>
    public int Sequence { get; set; }

    public int FuelStationId { get; set; }
    public FuelStation? FuelStation { get; set; }
}
