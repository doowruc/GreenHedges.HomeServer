using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit;

public class HoldingsTests
{
    private static FundDeal Buy(string fund, int day, decimal units, decimal price) =>
        new(fund, new DateOnly(2020, 6, day), TransactionKind.Purchase, units, price, -Math.Round(units * price / 100, 2));

    private static FundDeal Sell(string fund, int day, decimal units, decimal price) =>
        new(fund, new DateOnly(2020, 6, day), TransactionKind.Sale, units, price, Math.Round(units * price / 100, 2));

    [Fact]
    public void Purchases_add_units_and_sales_take_them_away_valued_at_the_latest_price()
    {
        var holding = Assert.Single(Holdings.Of([Buy("Fund A", 1, 100, 200), Sell("Fund A", 20, 30, 250), Buy("Fund A", 10, 50, 220)]));

        Assert.Equal(("Fund A", 3, 120m), (holding.Fund, holding.Deals, holding.Units));
        Assert.Equal(200m + 110m - 75m, holding.Cost);
        Assert.Equal((250m, new DateOnly(2020, 6, 20)), (holding.Price, holding.PriceDate));
        Assert.Equal(300m, holding.Value);
        Assert.Equal(65m, holding.Gain);
    }

    [Fact]
    public void Funds_are_largest_first_and_sold_out_last_and_other_kinds_are_ignored()
    {
        var holdings = Holdings.Of(
        [
            Buy("Small", 1, 10, 100), Buy("Gone", 1, 10, 100), Sell("Gone", 2, 10, 120), Buy("big", 1, 100, 100), Buy("Big", 3, 1, 100),
            new FundDeal("Small", new DateOnly(2020, 6, 5), TransactionKind.Rebate, 99, 100, 99),
        ]);

        Assert.Equal(["big", "Small", "Gone"], holdings.Select(h => h.Fund));   // a fund's name ignoring case
        Assert.Equal([101m, 10m, 0m], holdings.Select(h => h.Units));
        Assert.True(holdings[2].SoldOut);
        Assert.Equal(-2m, holdings[2].Cost);   // sold for more than it cost
    }

    [Fact]
    public void Without_a_price_there_is_no_value()
    {
        var holding = Assert.Single(Holdings.Of([new FundDeal("Fund A", new DateOnly(2020, 6, 1), TransactionKind.Purchase, 10, null, -20)]));

        Assert.Equal((10m, 20m, (decimal?)null, (decimal?)null), (holding.Units, holding.Cost, holding.Price, holding.Value));
    }
}
