using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class MileageTrendTests
{
    private static readonly DateOnly Latest = new(2026, 10, 1);

    [Fact]
    public void Uses_the_readings_from_the_last_6_months()
    {
        var trend = MileageTrend.From([
            (Latest.AddMonths(-12), 10000),   // older: ignored, though it would give a different rate
            (Latest.AddMonths(-6), 30000),
            (Latest.AddMonths(-3), 33000),
            (Latest, 36000)
        ])!;
        var days = Latest.DayNumber - Latest.AddMonths(-6).DayNumber;
        Assert.Equal((Latest, 36000), (trend.LatestDate, trend.LatestOdometer));
        Assert.Equal(6000.0 / days, trend.MilesPerDay, 6);
        Assert.Equal(6000.0 / days * 365.25 / 12, trend.MilesPerMonth, 6);
    }

    [Fact]
    public void Falls_back_to_all_the_readings_when_the_recent_ones_are_too_close_together()
    {
        var trend = MileageTrend.From([(Latest.AddMonths(-9), 20000), (Latest.AddDays(-10), 28700), (Latest, 29000)])!;
        Assert.Equal(9000.0 / (Latest.DayNumber - Latest.AddMonths(-9).DayNumber), trend.MilesPerDay, 6);
    }

    [Theory]
    [MemberData(nameof(NoTrend))]
    public void Needs_two_readings_far_enough_apart_with_the_miles_going_up((DateOnly, int)[] readings) =>
        Assert.Null(MileageTrend.From(readings));

    public static TheoryData<(DateOnly, int)[]> NoTrend => new()
    {
        Array.Empty<(DateOnly, int)>(),
        new[] { (Latest, 1000) },
        new[] { (Latest.AddDays(-MileageTrend.MinDays + 1), 1000), (Latest, 2000) },
        new[] { (Latest.AddMonths(-2), 5000), (Latest, 5000) },
        new[] { (Latest.AddMonths(-2), 5000), (Latest, 4000) }
    };

    [Fact]
    public void Estimates_when_a_mileage_is_reached_or_was()
    {
        var trend = new MileageTrend(Latest, 40000, 30);
        Assert.Equal(Latest.AddDays(100), trend.DateAt(43000));
        Assert.Equal(Latest.AddDays(-10), trend.DateAt(39700));
    }

    [Fact]
    public void MOT_readings_in_kilometres_are_converted_to_miles()
    {
        Assert.Equal((62137, 100000), (MileageTrend.Miles(100000, true), MileageTrend.Miles(100000, false)));
        Assert.Equal(62137, new MotTestView { Odometer = 100000, OdometerInKilometres = true }.Miles);
        Assert.Null(new MotTestView().Miles);
    }

    [Fact]
    public void Fill_ups_and_MOT_readings_are_used_together()
    {
        var trend = MileageTrend.From([new FuelEntryView { Date = Latest, Odometer = 36000 }], [(Latest.AddMonths(-3), 33000)])!;
        Assert.Equal(3000.0 / (Latest.DayNumber - Latest.AddMonths(-3).DayNumber), trend.MilesPerDay, 6);
    }

    [Fact]
    public void A_far_off_mileage_is_capped() =>
        Assert.Equal(Latest.AddDays(36500), new MileageTrend(Latest, 0, 0.001).DateAt(9_999_999));

    [Fact]
    public void Estimates_the_miles_a_year_to_the_nearest_100()
    {
        Assert.Equal(11000, new MileageTrend(Latest, 40000, 30).MilesPerYear);   // 10,957.5
        Assert.Equal(3700, new MileageTrend(Latest, 40000, 10).MilesPerYear);   // 3,652.5
    }

    [Fact]
    public void Suggests_the_mileage_for_a_valuation_from_the_latest_reading_and_the_trend()
    {
        // 30 miles a day since the latest reading of 40,000: 100 days on is 43,000; rounded to the nearest 100.
        (DateOnly, int)[] readings = [(Latest.AddDays(-100), 37000), (Latest, 40000)];
        Assert.Equal(43000, MileageTrend.SuggestedMileage(readings, Latest.AddDays(100)));
        Assert.Equal(40200, MileageTrend.SuggestedMileage(readings, Latest.AddDays(6)));   // 40,180
        Assert.Equal(40000, MileageTrend.SuggestedMileage(readings, Latest));   // the reading itself, unrounded
        Assert.Equal(40000, MileageTrend.SuggestedMileage(readings, Latest.AddDays(-5)));

        // Without a trend (readings too close together), the latest reading as it is.
        Assert.Equal(40123, MileageTrend.SuggestedMileage([(Latest.AddDays(-3), 40000), (Latest, 40123)], Latest.AddDays(50)));
        Assert.Null(MileageTrend.SuggestedMileage([], Latest));
    }
}
