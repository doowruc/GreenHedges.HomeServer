using GreenHedges.HomeServer.Presents.Services;

namespace GreenHedges.HomeServer.Presents.Tests.Unit;

public class SummaryTests
{
    private static readonly SummaryChild Ash = new(1, "Ash"), Bea = new(2, "Bea"), Cal = new(3, "Cal");

    private static SummaryPurchase P(SummaryChild child, decimal price, int quantity = 1, bool mum = false, bool birthday = false) =>
        new(child.Id, price, quantity, mum, birthday);

    private static List<(string, decimal, decimal, decimal)> Rows(ChristmasSummary s) =>
        s.Children.Select(c => (c.Name, c.Spent, c.Cash, c.Total)).ToList();

    [Fact]
    public void Without_a_budget_cash_tops_the_others_up_to_whoever_had_most()
    {
        var s = Summary.For(2025, null, [Ash, Bea], [P(Ash, 100m), P(Ash, 50m), P(Bea, 120m)]);

        Assert.Equal([("Ash", 150m, 0m, 150m), ("Bea", 120m, 30m, 150m)], Rows(s));
        Assert.Equal((150m, 270m, 30m, 300m), (s.Target, s.Spent, s.Cash, s.Total));
        Assert.False(s.OverBudget);
    }

    [Fact]
    public void Shares_count_not_prices()
    {
        var s = Summary.For(2025, null, [Ash, Bea], [P(Ash, 800m, mum: true, birthday: true), P(Bea, 9.99m, 2), P(Bea, 9.99m, mum: true)]);

        // Ash: 800 ÷ 4 = 200. Bea: 19.98 + 5.00 (4.995, half a penny up) = 24.98.
        Assert.Equal([("Ash", 200m, 0m, 200m), ("Bea", 24.98m, 175.02m, 200m)], Rows(s));
        Assert.Equal([1, 3], s.Children.Select(c => c.Presents));
    }

    [Fact]
    public void With_a_budget_above_them_both_get_cash_to_reach_it()
    {
        var s = Summary.For(2024, 375m, [Ash, Bea], [P(Ash, 250.10m), P(Bea, 350.25m)]);

        Assert.Equal([("Ash", 250.10m, 124.90m, 375m), ("Bea", 350.25m, 24.75m, 375m)], Rows(s));
        Assert.Equal(375m, s.Target);
        Assert.False(s.OverBudget);
    }

    [Fact]
    public void Over_budget_everyone_is_topped_up_to_the_highest_and_it_says_who()
    {
        var s = Summary.For(2024, 375m, [Ash, Bea], [P(Ash, 420m), P(Bea, 300m)]);

        Assert.Equal([("Ash", 420m, 0m, 420m), ("Bea", 300m, 120m, 420m)], Rows(s));
        Assert.True(s.OverBudget);
        Assert.Equal([true, false], s.Children.Select(c => c.OverBudget));
    }

    [Fact]
    public void Spending_exactly_the_budget_isnt_over_it() =>
        Assert.False(Summary.For(2024, 375m, [Ash], [P(Ash, 375m)]).OverBudget);

    [Fact]
    public void A_child_with_nothing_that_year_still_counts_and_order_is_kept()
    {
        var s = Summary.For(2025, null, [Cal, Ash, Bea], [P(Ash, 10m)]);

        Assert.Equal([("Cal", 0m, 10m, 10m), ("Ash", 10m, 0m, 10m), ("Bea", 0m, 10m, 10m)], Rows(s));
        Assert.Equal([0, 1, 0], s.Children.Select(c => c.Presents));
    }

    [Fact]
    public void A_year_with_no_presents_is_all_cash_with_a_budget_and_nothing_without()
    {
        Assert.Equal([("Ash", 0m, 50m, 50m), ("Bea", 0m, 50m, 50m)], Rows(Summary.For(2030, 50m, [Ash, Bea], [])));
        Assert.Equal([("Ash", 0m, 0m, 0m), ("Bea", 0m, 0m, 0m)], Rows(Summary.For(2030, null, [Ash, Bea], [])));
    }

    [Fact]
    public void No_children_is_an_empty_summary()
    {
        var s = Summary.For(2025, 100m, [], [P(Ash, 10m)]);

        Assert.Empty(s.Children);
        Assert.Equal((100m, 0m, 0m), (s.Target, s.Spent, s.Total));
    }
}
