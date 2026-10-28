using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Models;

/// <summary>A fuel brand, e.g. Shell. Each user has their own list.</summary>
/// <remarks>Fill-ups link to a <see cref="FuelStation"/>, and get their brand through it.</remarks>
public class FuelBrand : AuditedEntity, IUserOwned, IFuelPlace
{
    public const int MaxNameLength = 50;

    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    [Required, StringLength(MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    public List<FuelStation> Stations { get; set; } = [];
}
