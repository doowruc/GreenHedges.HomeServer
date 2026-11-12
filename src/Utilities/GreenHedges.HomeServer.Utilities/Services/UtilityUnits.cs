using System.Globalization;
using GreenHedges.HomeServer.Utilities.Models;

namespace GreenHedges.HomeServer.Utilities.Services;

/// <summary>How utilities and units are shown.</summary>
public static class UtilityUnits
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>What a utility's meter usually counts.</summary>
    public static MeterUnit DefaultUnit(this UtilityType utility) =>
        utility == UtilityType.Electricity ? MeterUnit.KilowattHours : MeterUnit.CubicMetres;

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

    /// <summary>What the daily figure is in: water in litres (1 m³ = 1,000), as the supplier shows it; the rest in the meter's unit.</summary>
    public static string PerDayUnit(this UtilityType utility, MeterUnit unit) =>
        utility == UtilityType.Water && unit == MeterUnit.CubicMetres ? "litres" : unit.Symbol();

    /// <summary>The daily figure as shown: whole litres for water, else two decimal places.</summary>
    public static string FormatPerDay(this UtilityType utility, MeterUnit unit, decimal? unitsPerDay) =>
        unitsPerDay is not { } perDay ? ""
        : utility == UtilityType.Water && unit == MeterUnit.CubicMetres ? (perDay * 1000).ToString("#,0", Uk)
        : perDay.ToString("#,0.00", Uk);

    /// <summary>A reading or units used: up to three decimal places, none padded.</summary>
    public static string FormatUnits(decimal? value) => value is { } v ? v.ToString("#,0.###", Uk) : "";
}
