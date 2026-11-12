using GreenHedges.HomeServer.Utilities.Models;

namespace GreenHedges.HomeServer.Utilities.Pages;

/// <summary>How pages show utilities and units.</summary>
public static class Units
{
    public static string Symbol(this MeterUnit unit) => unit switch
    {
        MeterUnit.KilowattHours => "kWh",
        MeterUnit.CubicMetres => "m³",
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    /// <summary>The utility's Bootstrap icon.</summary>
    public static string Icon(this UtilityType utility) => utility switch
    {
        UtilityType.Electricity => "bi-lightning-charge",
        UtilityType.Gas => "bi-fire",
        UtilityType.Water => "bi-droplet",
        _ => throw new ArgumentOutOfRangeException(nameof(utility))
    };
}
