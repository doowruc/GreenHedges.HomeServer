using System.Text.Json;
using GreenHedges.HomeServer.Utilities.Models;
using GreenHedges.HomeServer.Utilities.Services;

namespace GreenHedges.HomeServer.Utilities.Tests.Unit;

public class MonthlyUsageTests
{
    private static readonly MeterInfo First = new(1, null, null);

    private static List<ReadingRow> Rows(params (DateOnly Date, decimal Value)[] readings) =>
        ReadingCalculations.Calculate([First], readings.Select((r, i) => new ReadingInfo(i + 1, 1, r.Date, r.Value)));

    private static decimal? Month(List<YearUsage> years, int year, int month) =>
        years.SingleOrDefault(y => y.Year == year)?.PerDay[month - 1];

    [Fact]
    public void A_month_is_the_units_between_the_readings_on_its_1st_and_the_next_months_over_the_days_between()
    {
        var years = MonthlyUsage.ByMonth(Rows((new(2021, 1, 1), 100m), (new(2021, 2, 1), 410m), (new(2021, 3, 1), 550m)));

        Assert.Equal(10m, Month(years, 2021, 1));   // 310 over 31 days
        Assert.Equal(5m, Month(years, 2021, 2));    // 140 over 28 days
        Assert.Null(Month(years, 2021, 3));         // nothing near 1 April
        Assert.Equal([2021], years.Select(y => y.Year));
    }

    [Fact]
    public void The_reading_closest_to_the_1st_stands_for_it_even_from_the_month_before()
    {
        // 30 August is closest to 1 September, and 3 October to 1 October: "September" is the 34 days between them.
        var years = MonthlyUsage.ByMonth(Rows((new(2021, 8, 2), 0m), (new(2021, 8, 30), 280m), (new(2021, 10, 3), 620m)));

        Assert.Equal(10m, Month(years, 2021, 8));   // 280 over 28 days
        Assert.Equal(10m, Month(years, 2021, 9));   // 340 over 34 days
    }

    [Fact]
    public void Readings_between_the_two_ends_are_added_up()
    {
        var years = MonthlyUsage.ByMonth(Rows((new(2021, 1, 1), 0m), (new(2021, 1, 15), 100m), (new(2021, 1, 20), 180m), (new(2021, 2, 1), 310m)));

        Assert.Equal(10m, Month(years, 2021, 1));
    }

    [Fact]
    public void Of_two_readings_as_close_to_the_1st_the_earlier_stands_for_it()
    {
        // 30 May and 3 June are both two days from 1 June.
        var years = MonthlyUsage.ByMonth(Rows((new(2021, 5, 1), 0m), (new(2021, 5, 30), 290m), (new(2021, 6, 3), 350m), (new(2021, 7, 1), 600m)));

        Assert.Equal(10m, Month(years, 2021, 5));          // to 30 May: 290 over 29 days
        Assert.Equal(310m / 32, Month(years, 2021, 6));    // from 30 May: 310 over 32 days
    }

    [Fact]
    public void A_month_with_no_reading_near_one_of_its_ends_has_no_figure()
    {
        // Nothing within ten days of 1 February or 1 March.
        var years = MonthlyUsage.ByMonth(Rows((new(2021, 1, 1), 0m), (new(2021, 2, 14), 400m), (new(2021, 4, 1), 900m), (new(2021, 5, 1), 1200m)));

        Assert.Null(Month(years, 2021, 1));
        Assert.Null(Month(years, 2021, 2));
        Assert.Null(Month(years, 2021, 3));
        Assert.Equal(10m, Month(years, 2021, 4));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void Near_the_1st_is_within_ten_days(int daysAfter, bool counts)
    {
        var years = MonthlyUsage.ByMonth(Rows((new(2021, 1, 1), 0m), (new DateOnly(2021, 2, 1).AddDays(daysAfter), 500m)));

        Assert.Equal(counts, Month(years, 2021, 1) is not null);
    }

    [Fact]
    public void Readings_near_only_one_1st_make_no_month()
    {
        // Both are near 1 February only: nothing stands for 1 January or 1 March.
        var years = MonthlyUsage.ByMonth(Rows((new(2021, 1, 25), 0m), (new(2021, 2, 6), 120m)));

        Assert.Empty(years);
    }

    [Fact]
    public void A_month_across_a_meter_change_counts_the_new_meter_from_its_start()
    {
        MeterInfo replacement = new(2, new DateOnly(2021, 1, 20), 0m);
        var rows = ReadingCalculations.Calculate([First, replacement],
        [
            new(1, 1, new(2021, 1, 1), 5000m), new(2, 1, new(2021, 1, 15), 5140m), new(3, 2, new(2021, 2, 1), 170m)
        ]);

        Assert.Equal(10m, Month(MonthlyUsage.ByMonth(rows), 2021, 1));   // 140 on the old meter and 170 on the new, over 31 days
    }

    [Fact]
    public void December_runs_into_the_next_year_and_each_year_has_its_own_twelve_months()
    {
        var years = MonthlyUsage.ByMonth(Rows(
            (new(2020, 12, 1), 0m), (new(2021, 1, 1), 620m), (new(2021, 2, 1), 930m), (new(2021, 12, 2), 4000m), (new(2022, 1, 1), 4300m)));

        Assert.Equal([2020, 2021], years.Select(y => y.Year));
        Assert.Equal(20m, Month(years, 2020, 12));
        Assert.Equal(10m, Month(years, 2021, 1));
        Assert.Equal(10m, Month(years, 2021, 12));
        Assert.Equal(12, years[0].PerDay.Count);
    }

    [Fact]
    public void Fewer_than_two_readings_have_no_months()
    {
        Assert.Empty(MonthlyUsage.ByMonth(Rows()));
        Assert.Empty(MonthlyUsage.ByMonth(Rows((new(2021, 1, 1), 100m))));
    }

    private static UtilityHistory History(UtilityType utility, params (DateOnly Date, decimal Value)[] readings) =>
        new(utility, [new MeterSummary(1, utility.DefaultUnit(), null, null, null, readings.Length)], Rows(readings));

    [Fact]
    public void The_chart_has_a_series_per_year_with_twelve_values_and_gaps_for_months_without_one()
    {
        var history = History(UtilityType.Gas,
            (new(2020, 1, 1), 0m), (new(2020, 2, 1), 310m), (new(2021, 1, 1), 2000m), (new(2021, 2, 1), 2155m));

        using var chart = JsonDocument.Parse(UtilityChart.ByMonth(history)!);
        var root = chart.RootElement;

        Assert.Equal(("m³", 2), (root.GetProperty("unit").GetString(), root.GetProperty("decimals").GetInt32()));
        var years = root.GetProperty("years").EnumerateArray().ToList();
        Assert.Equal([2020, 2021], years.Select(y => y.GetProperty("year").GetInt32()));
        var first = years[0].GetProperty("values").EnumerateArray().ToList();
        Assert.Equal(12, first.Count);
        Assert.Equal(10m, first[0].GetDecimal());
        Assert.Equal(JsonValueKind.Null, first[1].ValueKind);
        Assert.Equal(5m, years[1].GetProperty("values")[0].GetDecimal());
    }

    [Fact]
    public void One_year_alone_has_nothing_to_compare_so_no_chart() =>
        Assert.Null(UtilityChart.ByMonth(History(UtilityType.Gas, (new(2021, 1, 1), 0m), (new(2021, 2, 1), 310m), (new(2021, 3, 1), 600m))));

    [Fact]
    public void Waters_months_are_in_litres()
    {
        var history = History(UtilityType.Water,
            (new(2020, 1, 1), 0m), (new(2020, 2, 1), 15.5m), (new(2021, 1, 1), 100m), (new(2021, 2, 1), 131m));

        using var chart = JsonDocument.Parse(UtilityChart.ByMonth(history)!);

        Assert.Equal("litres", chart.RootElement.GetProperty("unit").GetString());
        Assert.Equal(500m, chart.RootElement.GetProperty("years")[0].GetProperty("values")[0].GetDecimal());
    }
}
