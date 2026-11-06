using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class MileageEstimateTests
{
    private static readonly MileageReading Mot = new(new DateOnly(2025, 3, 1), 30000, "the MOT");
    private static readonly MileageReading FillUp = new(new DateOnly(2025, 3, 31), 31500, "a fill-up");

    [Fact]
    public void Between_two_readings_it_goes_by_the_days()
    {
        var estimate = MileageEstimate.For([FillUp, Mot], new DateOnly(2025, 3, 11))!;
        Assert.Equal(30500, estimate.Mileage);
        Assert.Equal("Between 30,000 miles at the MOT on 01/03/2025 and 31,500 miles at a fill-up on 31/03/2025.", estimate.Basis);
    }

    [Fact]
    public void Uses_the_nearest_readings_either_side()
    {
        var older = new MileageReading(new DateOnly(2024, 1, 1), 10000, "a service");
        Assert.Equal(30500, MileageEstimate.For([older, Mot, FillUp], new DateOnly(2025, 3, 11))!.Mileage);
    }

    [Fact]
    public void A_reading_on_the_day_is_used_as_it_is()
    {
        var estimate = MileageEstimate.For([Mot, FillUp], Mot.Date)!;
        Assert.Equal((30000, "The reading at 30,000 miles at the MOT on 01/03/2025."), (estimate.Mileage, estimate.Basis));
    }

    [Fact]
    public void After_the_readings_it_carries_on_at_the_recent_rate()
    {
        var estimate = MileageEstimate.For([Mot, FillUp], FillUp.Date.AddDays(10))!;   // 50 miles a day
        Assert.Equal(32000, estimate.Mileage);
        Assert.Equal("On from 31,500 miles at a fill-up on 31/03/2025, at 1,522 miles a month.", estimate.Basis);
    }

    [Fact]
    public void Before_the_readings_it_goes_back_at_the_early_rate_but_not_below_nothing()
    {
        var later = new MileageReading(new DateOnly(2026, 3, 1), 31600, "the MOT");   // slower later on: not used
        var estimate = MileageEstimate.For([Mot, FillUp, later], Mot.Date.AddDays(-20))!;
        Assert.Equal(29000, estimate.Mileage);
        Assert.StartsWith("Back from 30,000 miles at the MOT on 01/03/2025, at 1,522 miles a month.", estimate.Basis);
        Assert.Equal(0, MileageEstimate.For([Mot, FillUp], Mot.Date.AddYears(-5))!.Mileage);
    }

    [Fact]
    public void Needs_readings_either_side_or_a_rate()
    {
        Assert.Null(MileageEstimate.For([], new DateOnly(2025, 3, 11)));
        Assert.Null(MileageEstimate.For([Mot], Mot.Date.AddDays(10)));
        Assert.Null(MileageEstimate.For([Mot], Mot.Date.AddDays(-10)));
        Assert.Null(MileageEstimate.For([Mot, Mot with { Date = Mot.Date.AddDays(10) }], Mot.Date.AddDays(-10)));   // too close together
    }
}
