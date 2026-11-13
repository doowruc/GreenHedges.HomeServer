using GreenHedges.HomeServer.Presents.Services;
using GreenHedges.HomeServer.Presents.Services.Import;

namespace GreenHedges.HomeServer.Presents.Tests.Unit;

public class PresentsImporterTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);

    private static SheetPurchase P(int row, string who, decimal amount, DateOnly? date = null) => new(row, date, "Shop", $"Present {row}", who, amount);

    private static SheetYear Year(int year, SheetPurchase[] purchases, params SheetCash[] cash) => new(year, [.. purchases], [.. cash], []);

    private static ImportPlan Plan(IReadOnlyList<SummaryChild> children, IReadOnlySet<int> years, params SheetYear[] sheets) =>
        PresentsImporter.Plan(new WorkbookContents([.. sheets], [], ["a warning"]), children, years, Today);

    [Fact]
    public void Names_match_children_here_ignoring_case_and_new_ones_are_added_in_the_order_first_named()
    {
        var plan = Plan([new SummaryChild(7, "Ash")], new HashSet<int>(),
            Year(2020, [P(2, "ash", 10m), P(3, "Cal", 5m)]),
            Year(2021, [P(2, "Bea", 4m), P(3, "CAL", 6m)], new SheetCash(4, "Dee", 1m)));

        Assert.Equal([new SummaryChild(-1, "Cal"), new SummaryChild(-2, "Bea"), new SummaryChild(-3, "Dee")], plan.NewChildren);
        Assert.Equal([7, -1], plan.Years[0].ToImport.Select(t => t.ChildId));
        Assert.Equal([-2, -1], plan.Years[1].ToImport.Select(t => t.ChildId));
        Assert.Equal(["a warning"], plan.Warnings);
        Assert.Equal(4, plan.Presents);
    }

    [Fact]
    public void A_year_already_here_is_left_alone_and_its_names_dont_add_children()
    {
        var plan = Plan([new SummaryChild(1, "Ash")], new HashSet<int> { 2020 },
            Year(2020, [P(2, "Bea", 10m)], new SheetCash(3, "Ash", 10m)));

        var year = Assert.Single(plan.Years);
        Assert.True(year.AlreadyThere);
        Assert.Empty(year.ToImport);
        Assert.Null(year.Summary);
        Assert.Equal(1, year.CashRows);
        Assert.Empty(plan.NewChildren);
        Assert.Empty(plan.ToImport);
    }

    [Fact]
    public void Cash_that_balanced_to_whoever_had_most_matches_and_sets_no_budget()
    {
        var year = Assert.Single(Plan([], new HashSet<int>(),
            Year(2023, [P(2, "Ash", 160m), P(3, "Ash", 9.98m), P(4, "Bea", 100m)], new SheetCash(5, "Bea", 69.98m))).Years);

        Assert.Null(year.Budget);
        Assert.Equal([new CashCheck("Ash", null, 0m), new CashCheck("Bea", 69.98m, 69.98m)], year.Cash);
        Assert.All(year.Cash, c => Assert.True(c.Matches));
        Assert.Equal(169.98m, year.Summary!.Target);
    }

    [Fact]
    public void Where_everyone_had_cash_and_equal_totals_that_was_the_budget()
    {
        var year = Assert.Single(Plan([], new HashSet<int>(),
            Year(2024, [P(2, "Ash", 180m), P(3, "Bea", 240.50m)], new SheetCash(4, "Ash", 70m), new SheetCash(5, "Bea", 9.50m))).Years);

        Assert.Equal(250m, year.Budget);
        Assert.Equal([new CashCheck("Ash", 70m, 70m), new CashCheck("Bea", 9.50m, 9.50m)], year.Cash);
    }

    [Fact]
    public void Cash_that_didnt_balance_shows_what_would_have()
    {
        var year = Assert.Single(Plan([], new HashSet<int>(),
            Year(2018, [P(2, "Ash", 120m), P(3, "Bea", 150m)], new SheetCash(4, "Ash", 20m))).Years);

        Assert.Null(year.Budget);
        var ash = year.Cash[0];
        Assert.Equal((20m, 30m, false), (ash.Sheet, ash.App, ash.Matches));
        Assert.Equal(new CashCheck("Bea", null, 0m), year.Cash[1]);
    }

    [Fact]
    public void Every_child_counts_in_every_year_and_a_future_date_is_dropped()
    {
        var plan = Plan([new SummaryChild(1, "Ash"), new SummaryChild(2, "Bea")], new HashSet<int>(),
            Year(2026, [P(2, "Ash", 30m, new DateOnly(2026, 12, 1)), P(3, "Ash", 5m, new DateOnly(2026, 11, 1))]));

        var year = Assert.Single(plan.Years);
        Assert.Equal([null, new DateOnly(2026, 11, 1)], year.ToImport.Select(t => t.Purchase.Date));
        Assert.Equal(new CashCheck("Bea", null, 35m), year.Cash[1]);
    }
}
