using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Services;
using System.Text.Json;

namespace GreenHedges.HomeServer.Utilities.Tests.Unit;

public class PolynomialTrendTests
{
    private static List<(double X, double Y)> Points(Func<double, double> f, params double[] xs) => xs.Select(x => (x, f(x))).ToList();

    [Fact]
    public void Points_on_an_order_4_polynomial_are_fitted_exactly()
    {
        static double F(double x) => 2 - 3 * x + 0.5 * x * x - 0.25 * x * x * x + 0.01 * x * x * x * x;

        var trend = PolynomialTrend.Fit(Points(F, 0, 1, 2, 3.5, 5, 7, 8, 11, 12))!;

        Assert.Equal(4, trend.Order);
        foreach (var x in new[] { 0, 0.5, 4, 9.25, 12 }) Assert.Equal(F(x), trend.At(x), 8);
    }

    [Fact]
    public void Large_x_values_such_as_day_numbers_dont_lose_the_fit()
    {
        // Ten years of days, numbered from the year 1 as DateOnly.DayNumber does (about 738,000).
        static double F(double x) { var t = (x - 738_000) / 365; return 12 + 3 * t - 1.5 * t * t + 0.2 * t * t * t - 0.008 * t * t * t * t; }
        var xs = Enumerable.Range(0, 100).Select(i => 738_000d + i * 37).ToArray();

        var trend = PolynomialTrend.Fit(Points(F, xs))!;

        foreach (var x in new[] { 738_000d, 739_234.5, 741_663 }) Assert.Equal(F(x), trend.At(x), 6);
    }

    [Fact]
    public void A_straight_line_is_its_own_trend_at_any_order()
    {
        var trend = PolynomialTrend.Fit(Points(x => 5 + 2 * x, 1, 2, 3, 4, 5, 6, 7), 4)!;

        Assert.Equal(5 + 2 * 3.3, trend.At(3.3), 8);
    }

    [Fact]
    public void Scattered_points_get_the_least_squares_fit()
    {
        // Order 1 through (0,0), (1,1), (2,0), (3,1): y = 0.2x + 0.2 by hand.
        var trend = PolynomialTrend.Fit([(0, 0), (1, 1), (2, 0), (3, 1)], 1)!;

        Assert.Equal(0.2, trend.At(0), 10);
        Assert.Equal(0.8, trend.At(3), 10);
    }

    [Theory]
    [InlineData(3, 2)]   // three points pin down a quadratic at most
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    [InlineData(9, 4)]
    public void Too_few_points_for_the_order_get_the_highest_order_they_can(int points, int order) =>
        Assert.Equal(order, PolynomialTrend.Fit(Points(x => x * x, Enumerable.Range(0, points).Select(i => (double)i).ToArray()), 4)!.Order);

    [Fact]
    public void Fewer_than_three_different_x_values_have_no_trend()
    {
        Assert.Null(PolynomialTrend.Fit([(1, 1), (2, 2)]));
        Assert.Null(PolynomialTrend.Fit([(1, 1), (1, 3), (2, 2), (2, 5)]));
        Assert.Null(PolynomialTrend.Fit([]));
    }

    private static UtilityHistory History(UtilityType utility, params (DateOnly Date, decimal Value)[] readings)
    {
        var meter = new MeterSummary(1, utility.DefaultUnit(), null, null, null, readings.Length);
        return new UtilityHistory(utility, [meter],
            ReadingCalculations.Calculate([meter.Info], readings.Select((r, i) => new ReadingInfo(i + 1, 1, r.Date, r.Value))));
    }

    private static readonly DateOnly Start = new(2021, 1, 1);

    [Fact]
    public void The_chart_has_units_a_day_at_each_readings_date_and_a_trend_across_them()
    {
        // 10 a day, then 20, then 30, then 40: a straight line by date.
        var history = History(UtilityType.Electricity,
            (Start, 0m), (Start.AddDays(10), 100m), (Start.AddDays(20), 300m), (Start.AddDays(30), 600m), (Start.AddDays(40), 1000m));

        using var chart = JsonDocument.Parse(UtilityChart.For(history)!);
        var root = chart.RootElement;

        Assert.Equal(("kWh", 2), (root.GetProperty("unit").GetString(), root.GetProperty("decimals").GetInt32()));
        Assert.Equal([("2021-01-11", 10m), ("2021-01-21", 20m), ("2021-01-31", 30m), ("2021-02-10", 40m)],
            root.GetProperty("points").EnumerateArray().Select(p => (p.GetProperty("date").GetString()!, p.GetProperty("value").GetDecimal())));
        Assert.Equal(3, root.GetProperty("trendOrder").GetInt32());   // four points: a cubic at most
        var trend = root.GetProperty("trend").EnumerateArray().ToList();
        Assert.Equal(("2021-01-11", 10m), (trend[0].GetProperty("date").GetString(), trend[0].GetProperty("value").GetDecimal()));
        Assert.Equal(("2021-02-10", 40m), (trend[^1].GetProperty("date").GetString(), trend[^1].GetProperty("value").GetDecimal()));
        Assert.Equal(31, trend.Count);   // a point a day at most
    }

    [Fact]
    public void Waters_chart_is_in_litres()
    {
        var history = History(UtilityType.Water, (Start, 100m), (Start.AddDays(10), 105m), (Start.AddDays(30), 113m));

        using var chart = JsonDocument.Parse(UtilityChart.For(history)!);

        Assert.Equal(("litres", 0), (chart.RootElement.GetProperty("unit").GetString(), chart.RootElement.GetProperty("decimals").GetInt32()));
        Assert.Equal([500m, 400m], chart.RootElement.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("value").GetDecimal()));
        Assert.Empty(chart.RootElement.GetProperty("trend").EnumerateArray());   // two points: a line with no trend
    }

    [Fact]
    public void Fewer_than_two_daily_figures_have_no_chart()
    {
        Assert.Null(UtilityChart.For(History(UtilityType.Gas)));
        Assert.Null(UtilityChart.For(History(UtilityType.Gas, (Start, 100m), (Start.AddDays(10), 105m))));
    }
}
