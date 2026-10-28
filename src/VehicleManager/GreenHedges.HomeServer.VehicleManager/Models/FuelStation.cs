using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>
/// A filling station: a brand at a location, e.g. Sainsbury's at Hill Top. Names are unique per
/// brand, so one location can have several stations ("Riverside" for Asda and for Morrisons). A
/// station's brand is fixed: a rebranded station is a new station, so past fill-ups keep their brand.
/// </summary>
public class FuelStation : AuditedEntity, IUserOwned, IFuelPlace
{
    public const int MaxNameLength = 100;

    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    public int FuelBrandId { get; set; }
    public FuelBrand? FuelBrand { get; set; }

    [Required, StringLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    public List<FuelEntryStop> Stops { get; set; } = [];
}
