using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>The free valuation sites the pre-renewal page links to, to look a vehicle's value up by hand.</summary>
public static class ValuationSites
{
    public record Site(ValuationSource Source, string Name, string Url);

    public static readonly IReadOnlyList<Site> All =
    [
        new(ValuationSource.Parkers, "Parkers", "https://www.parkers.co.uk/pricing/"),
        new(ValuationSource.AutoTrader, "Auto Trader", "https://www.autotrader.co.uk/car-valuation"),
        new(ValuationSource.WhatCar, "What Car?", "https://www.whatcar.com/car-valuation/")
    ];
}
