using System.ComponentModel.DataAnnotations;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.Utilities.Models;

/// <summary>What a meter measures.</summary>
public enum UtilityType
{
    Electricity,
    Gas,
    Water
}

/// <summary>What a meter's readings count.</summary>
public enum MeterUnit
{
    /// <summary>kWh: electricity.</summary>
    KilowattHours,

    /// <summary>m³: gas (billed in kWh, worked out with each bill's calorific value) and water.</summary>
    CubicMetres
}

/// <summary>
/// One physical meter for a utility: the user-owned root. A utility has had several over the years, each with its
/// own run of readings; a replacement starts counting again, usually from 0.
/// </summary>
public class Meter : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    public UtilityType Utility { get; set; }

    public MeterUnit Unit { get; set; }

    /// <summary>As printed on the meter and the bills. Optional.</summary>
    [StringLength(50)]
    public string? SerialNumber { get; set; }

    /// <summary>
    /// When it replaced the meter before it. Null for a utility's first meter here, whose start isn't known: its
    /// first reading is only where counting starts.
    /// </summary>
    public DateOnly? InstalledOn { get; set; }

    /// <summary>
    /// What it showed when installed (usually 0), so its first reading has units used. Null for a utility's first
    /// meter, as <see cref="InstalledOn"/>.
    /// </summary>
    public decimal? StartReading { get; set; }

    public List<Reading> Readings { get; set; } = [];
}
