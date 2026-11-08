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
        Assert.Equal(248m, holding.Cost);   // 310 for 150 units, less the 30 sold's share: not the 75 they raised
        Assert.Equal((250m, new DateOnly(2020, 6, 20)), (holding.Price, holding.PriceDate));
        Assert.Equal(300m, holding.Value);
        Assert.Equal(52m, holding.Gain);
        Assert.Equal(20.97m, Math.Round(holding.GainPercent!.Value, 2));   // 52 on 248
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
        Assert.Equal(0m, holdings[2].Cost);   // nothing held, however much it was sold for
        Assert.Null(holdings[2].GainPercent);   // no cost to be a percentage of
    }

    [Fact]
    public void A_conversion_moves_the_units_and_cost_to_the_new_fund()
    {
        var holdings = Holdings.Of(
        [
            Buy("Class A", 1, 100, 200),
            new FundDeal("Class A", new DateOnly(2020, 6, 10), TransactionKind.Conversion, -100, null, 200),
            new FundDeal("Class I", new DateOnly(2020, 6, 10), TransactionKind.Conversion, 95, null, -200),
            Buy("Class I", 20, 10, 220),
        ]);

        var (classI, classA) = (holdings[0], holdings[1]);
        Assert.Equal(("Class I", 105m, 222m, 231m), (classI.Fund, classI.Units, classI.Cost, classI.Value));   // 105 at 220p
        Assert.False(classI.Converted);
        Assert.Equal((0m, 0m, true, true), (classA.Units, classA.Cost, classA.SoldOut, classA.Converted));
    }

    [Fact]
    public void A_sale_takes_its_share_of_the_cost_in_date_order_and_units_bought_that_day_count()
    {
        var holding = Assert.Single(Holdings.Of([Sell("Fund A", 5, 50, 300), Buy("Fund A", 5, 50, 100), Buy("Fund A", 1, 50, 300)]));

        Assert.Equal((50m, 100m), (holding.Units, holding.Cost));   // 150 + 50 = 200 for 100 units; half of them sold
    }

    [Fact]
    public void Without_a_price_there_is_no_value()
    {
        var holding = Assert.Single(Holdings.Of([new FundDeal("Fund A", new DateOnly(2020, 6, 1), TransactionKind.Purchase, 10, null, -20)]));

        Assert.Equal((10m, 20m, (decimal?)null, (decimal?)null), (holding.Units, holding.Cost, holding.Price, holding.Value));
        Assert.Null(holding.GainPercent);
    }
}
