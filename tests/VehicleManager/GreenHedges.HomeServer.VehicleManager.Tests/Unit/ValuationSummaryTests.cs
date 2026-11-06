using GreenHedges.HomeServer.VehicleManager.Models;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class ValuationSummaryTests
{
    private static readonly DateOnly Day = new(2026, 11, 6);
    private static int nextId;

    private static VehicleValuationView Value(int daysAgo, decimal value, ValuationType type, ValuationGrade? grade, ValuationSource source) =>
        new() { Id = ++nextId, Date = Day.AddDays(-daysAgo), Value = value, Type = type, Grade = grade, Source = source };

    [Fact]
    public void Groups_the_latest_round_by_type_and_grade_with_each_sources_figure_and_their_average()
    {
        var summary = ValuationSummary.For(
        [
            Value(0, 8000, ValuationType.Trade, ValuationGrade.Average, ValuationSource.Parkers),
            Value(1, 8400, ValuationType.Trade, ValuationGrade.Average, ValuationSource.AutoTrader),
            Value(2, 7000, ValuationType.Trade, ValuationGrade.Poor, ValuationSource.Parkers),
            Value(0, 9900, ValuationType.Private, null, ValuationSource.WhatCar),
            Value(5, 9700, ValuationType.Private, null, ValuationSource.WhatCar),   // an earlier look-up on the same site: the later one counts
            Value(3, 9500, ValuationType.Private, null, ValuationSource.Parkers)
        ])!;

        Assert.Equal((Day.AddDays(-5), Day), (summary.From, summary.To));
        Assert.Equal([ValuationSource.Parkers, ValuationSource.AutoTrader, ValuationSource.WhatCar], summary.Sources);
        Assert.Equal(["Private", "Trade (Poor)", "Trade (Average)"], summary.Rows.Select(r => r.Label));
        Assert.Equal([9700m, 7000m, 8200m], summary.Rows.Select(r => r.Average));
        Assert.Equal(9900m, summary.Rows[0].BySource[ValuationSource.WhatCar].Value);
        Assert.False(summary.Rows[1].BySource.ContainsKey(ValuationSource.AutoTrader));
        Assert.All(summary.Rows, r => Assert.Null(r.PreviousAverage));
        Assert.Null(summary.PreviousTo);
    }

    [Fact]
    public void Compares_each_type_and_grade_with_the_previous_rounds_average()
    {
        var summary = ValuationSummary.For(
        [
            Value(0, 8000, ValuationType.Trade, ValuationGrade.Average, ValuationSource.Parkers),
            Value(10, 8400, ValuationType.Trade, ValuationGrade.Average, ValuationSource.AutoTrader),
            Value(0, 9900, ValuationType.Retail, null, ValuationSource.Parkers),
            // A year before: the previous round, which runs up to 30 days back from its own newest value.
            Value(365, 9000, ValuationType.Trade, ValuationGrade.Average, ValuationSource.Parkers),
            Value(380, 9400, ValuationType.Trade, ValuationGrade.Average, ValuationSource.AutoTrader),
            Value(365, 6000, ValuationType.Trade, ValuationGrade.Poor, ValuationSource.Parkers),   // not in this year's round
            Value(400, 1, ValuationType.Retail, null, ValuationSource.Parkers)   // older than the previous round
        ])!;

        Assert.Equal((Day.AddDays(-380), Day.AddDays(-365)), (summary.PreviousFrom, summary.PreviousTo));
        Assert.Equal(["Retail", "Trade (Average)"], summary.Rows.Select(r => r.Label));
        Assert.Null(summary.Rows[0].PreviousAverage);
        Assert.Equal((8200m, 9200m, -1000m), (summary.Rows[1].Average, summary.Rows[1].PreviousAverage!.Value, summary.Rows[1].Change!.Value));
    }

    [Fact]
    public void Values_30_days_before_the_newest_are_a_round_of_their_own()
    {
        var summary = ValuationSummary.For(
        [
            Value(0, 8000, ValuationType.Trade, null, ValuationSource.Parkers),
            Value(29, 8100, ValuationType.Trade, null, ValuationSource.AutoTrader),
            Value(30, 9000, ValuationType.Trade, null, ValuationSource.WhatCar)
        ])!;
        Assert.Equal(8050m, Assert.Single(summary.Rows).Average);
        Assert.Equal(9000m, summary.Rows[0].PreviousAverage);
        Assert.Null(ValuationSummary.For([]));
    }
}
