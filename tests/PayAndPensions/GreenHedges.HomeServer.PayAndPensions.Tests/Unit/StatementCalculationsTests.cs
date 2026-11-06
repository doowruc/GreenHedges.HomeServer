using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit;

public class StatementCalculationsTests
{
    private static readonly DateOnly Start = new(2020, 1, 1);

    private static List<StatementValue> Statements(params (int Days, decimal Value)[] statements) =>
        statements.Select(s => new StatementValue(Start.AddDays(s.Days), s.Value)).ToList();

    [Fact]
    public void Each_statement_changes_on_the_one_before_and_the_first_on_0_at_the_start()
    {
        var figures = StatementCalculations.Calculate(Start, Statements((100, 1000m), (200, 1500m), (250, 1200m)));

        Assert.Equal([1000m, 500m, -300m], figures.Select(f => f.Change));
        Assert.Equal([null, 0.5m, -0.2m], figures.Select(f => f.Growth));   // change ÷ previous; none from 0
        Assert.Equal([100, 100, 50], figures.Select(f => f.PeriodDays));
        Assert.Equal([10m, 5m, -6m], figures.Select(f => f.ChangePerDay));
        Assert.Equal([100, 200, 250], figures.Select(f => f.DaysSinceStart));
        Assert.Equal([10m, 7.5m, 4.8m], figures.Select(f => f.PerDaySinceStart));
    }

    [Fact]
    public void Below_highest_is_against_the_highest_of_all()
    {
        var figures = StatementCalculations.Calculate(Start, Statements((100, 1000m), (200, 1500m), (250, 1200m)));

        Assert.Equal([-500m, 0m, -300m], figures.Select(f => f.BelowHighest));
    }

    [Fact]
    public void Without_a_start_date_the_first_has_no_change_and_nothing_counts_from_the_start()
    {
        var figures = StatementCalculations.Calculate(null, Statements((100, 1000m), (200, 1100m)));

        Assert.Equal([null, 100m], figures.Select(f => f.Change));
        Assert.Equal([null, 0.1m], figures.Select(f => f.Growth));
        Assert.Equal([null, 100], figures.Select(f => f.PeriodDays));
        Assert.All(figures, f => Assert.Null(f.DaysSinceStart));
        Assert.All(figures, f => Assert.Null(f.PerDaySinceStart));
    }

    [Fact]
    public void A_statement_on_the_start_date_has_no_per_day_figures()
    {
        var figures = StatementCalculations.Calculate(Start, Statements((0, 500m)));

        Assert.Equal((500m, 0, null, null), (figures[0].Change, figures[0].PeriodDays, figures[0].ChangePerDay, figures[0].PerDaySinceStart));
    }

    [Fact]
    public void No_statements_no_figures() => Assert.Empty(StatementCalculations.Calculate(Start, []));
}
