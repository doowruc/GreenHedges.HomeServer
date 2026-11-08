using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit;

/// <summary>What the dashboard shows for a pension: made-up figures.</summary>
public class PensionOverviewTests
{
    private static readonly DateOnly Spring = new(2026, 4, 5);
    private static readonly DateOnly Summer = new(2026, 7, 5);
    private static readonly DateOnly Today = new(2026, 11, 1);

    private static FundHolding Held(decimal value, decimal cost, DateOnly date) => new("Fund", 1, 100m, cost, 200m, date, value);

    private static PensionSummary Of(FundHolding[] holdings, params StatementSummary[] statements) =>
        PensionOverview.Of(1, "SIPP", "Example Pensions", holdings, statements);

    [Fact]
    public void Holdings_value_it_over_a_statement_with_their_gain()
    {
        var pension = Of([Held(1500m, 1000m, Today), Held(500m, 600m, Today.AddDays(-2))], new StatementSummary(Summer, 1800m));

        Assert.Equal(2000m, pension.Value);
        Assert.Equal(Today.AddDays(-2), pension.ValueDate);    // the oldest price
        Assert.True(pension.FromHoldings);
        Assert.Equal(1600m, pension.Cost);
        Assert.Equal(400m, pension.Gain);
        Assert.Equal(25m, pension.GainPercent);
    }

    [Fact]
    public void Without_holdings_its_latest_statement_values_it_and_there_is_no_gain()
    {
        var pension = Of([], new StatementSummary(Spring, 1000m), new StatementSummary(Summer, 1100m));

        Assert.Equal(1100m, pension.Value);
        Assert.Equal(Summer, pension.ValueDate);
        Assert.False(pension.FromHoldings);
        Assert.Null(pension.Gain);
        Assert.Null(pension.GainPercent);
    }

    [Fact]
    public void With_nothing_it_has_no_value()
    {
        var pension = Of([]);

        Assert.Null(pension.Value);
        Assert.Null(pension.ValueDate);
        Assert.Empty(pension.History);
    }

    [Fact]
    public void Sold_out_funds_leave_it_to_the_statement()
    {
        var soldOut = new FundHolding("Old fund", 2, 0m, 0m, 200m, Today, 0m);

        var pension = Of([soldOut], new StatementSummary(Summer, 1100m));

        Assert.Equal(1100m, pension.Value);
        Assert.False(pension.FromHoldings);
    }

    [Fact]
    public void A_fund_held_without_a_price_leaves_it_to_the_statement_rather_than_undercounting()
    {
        var unpriced = new FundHolding("New fund", 1, 50m, 100m, null, null, null);

        var pension = Of([Held(1500m, 1000m, Today), unpriced], new StatementSummary(Summer, 1800m));

        Assert.Equal(1800m, pension.Value);
        Assert.False(pension.FromHoldings);
    }

    [Fact]
    public void The_history_is_the_statements_then_a_more_recent_holdings_value()
    {
        var pension = Of([Held(1500m, 1000m, Today)], new StatementSummary(Spring, 1000m), new StatementSummary(Summer, 1100m));

        Assert.Equal([new(Spring, 1000m), new(Summer, 1100m), new(Today, 1500m)], pension.History);
    }

    [Fact]
    public void A_holdings_value_no_newer_than_the_last_statement_adds_no_point()
    {
        var pension = Of([Held(1500m, 1000m, Summer)], new StatementSummary(Summer, 1100m));

        Assert.Equal([new(Summer, 1100m)], pension.History);
    }

    [Fact]
    public void Two_statements_on_a_date_are_one_point()
    {
        var pension = Of([], new StatementSummary(Summer, 1100m), new StatementSummary(Summer, 1105m));

        Assert.Equal([new(Summer, 1105m)], pension.History);
    }

    [Fact]
    public void The_projection_is_the_latest_statement_with_one()
    {
        var pension = Of([], new StatementSummary(Spring, 1000m, 50000m, 2000m), new StatementSummary(Summer, 1100m, 55000m, 2200m), new StatementSummary(Today, 1200m));

        Assert.Equal(new PensionProjection(Summer, 55000m, 2200m), pension.Projection);
    }

    [Fact]
    public void A_persons_totals_count_each_pension_once_and_the_gain_only_from_holdings()
    {
        var sipp = PensionOverview.Of(1, "SIPP", "A", [Held(2000m, 1600m, Today)], [new StatementSummary(Summer, 1800m, 90000m, 3000m)]);
        var workplace = PensionOverview.Of(2, "Workplace", "B", [], [new StatementSummary(Spring, 1000m, 10000m, 500m)]);
        var empty = PensionOverview.Of(3, "New", "C", [], []);

        var all = new PersonPensions([sipp, workplace, empty]);

        Assert.Equal(2, all.ValuedCount);
        Assert.Equal(3000m, all.TotalValue);
        Assert.Equal(Spring, all.OldestValueDate);
        Assert.Equal(1, all.WithHoldingsCount);
        Assert.Equal(1600m, all.TotalCost);
        Assert.Equal(400m, all.TotalGain);
        Assert.Equal(25m, all.TotalGainPercent);
        Assert.Equal(100000m, all.ProjectedFund);
        Assert.Equal(3500m, all.ProjectedAnnualIncome);
        Assert.Equal(Spring, all.OldestProjectionDate);
    }
}
