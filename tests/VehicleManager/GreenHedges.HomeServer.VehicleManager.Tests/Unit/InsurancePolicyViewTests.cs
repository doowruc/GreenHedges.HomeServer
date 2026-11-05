using GreenHedges.HomeServer.VehicleManager.Pages.Insurance;
using GreenHedges.HomeServer.VehicleManager.Services;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class InsurancePolicyViewTests
{
    private static readonly DateTime Start = new(2025, 11, 5), End = new(2026, 11, 4, 23, 59, 0);

    private static InsurancePeriodView Covered(int vehicleId, DateTime from, DateTime to, decimal? adjustment = null) =>
        new() { VehicleId = vehicleId, Make = "Car", Model = vehicleId.ToString(), RegistrationNumber = $"REG {vehicleId}", From = from, To = to, Adjustment = adjustment };

    private static InsurancePolicyView Policy(DateTime start, DateTime end, decimal premium = 400m, params InsurancePeriodView[] vehicles) =>
        new() { StartDate = start, EndDate = end, Premium = premium, Vehicles = [.. vehicles] };

    [Fact]
    public void The_renewal_date_is_the_last_day_covered()
    {
        Assert.Equal(new DateOnly(2026, 11, 4), InsurancePolicyView.LastDay(new DateTime(2026, 11, 4, 23, 59, 0)));
        Assert.Equal(new DateOnly(2026, 11, 5), InsurancePolicyView.LastDay(new DateTime(2026, 11, 5, 14, 32, 0)));
        // Midnight is the start of the 5th, so the 4th was the last day.
        Assert.Equal(new DateOnly(2026, 11, 4), InsurancePolicyView.LastDay(new DateTime(2026, 11, 5, 0, 0, 0)));
    }

    [Fact]
    public void A_switched_policys_premium_is_shared_by_time_on_it_plus_each_vehicles_adjustment()
    {
        var switchAt = new DateTime(2026, 3, 5, 12, 0, 0);   // about a third of the way through
        var first = Covered(1, Start, switchAt);
        var second = Covered(2, switchAt, End, adjustment: 30m);
        var policy = Policy(Start, End, 450m, first, second);

        var (a, b) = (InsuranceCosts.Share(policy, first), InsuranceCosts.Share(policy, second));
        Assert.InRange(a, 145m, 155m);
        Assert.Equal(450m + 30m, a + b);
        Assert.Equal(480m, policy.TotalCost);
        Assert.Equal([2], policy.VehiclesAtEnd.Select(v => v.VehicleId));
        // The new car's few months cost about £330, which is about £490 a year.
        Assert.InRange(InsuranceCosts.Yearly(b, second.Days), 480m, 500m);
    }

    [Fact]
    public void A_multi_car_policy_shares_its_premium_evenly_between_cars_on_it_all_year()
    {
        var a = Covered(1, Start, End);
        var b = Covered(2, Start, End);
        var policy = Policy(Start, End, 600m, a, b);
        Assert.Equal((300m, 300m), (InsuranceCosts.Share(policy, a), InsuranceCosts.Share(policy, b)));
        Assert.Equal(2, policy.VehiclesAtEnd.Count());
    }

    [Fact]
    public void The_chart_has_a_line_per_vehicle_and_none_with_a_single_point()
    {
        var older = Policy(Start.AddYears(-1), Start, 400m, Covered(1, Start.AddYears(-1), Start));
        var newer = Policy(Start, End, 450m, Covered(1, Start, End));
        Assert.Null(InsuranceCosts.ChartJson([newer]));
        var json = InsuranceCosts.ChartJson([newer, older])!;
        Assert.StartsWith("[{\"vehicle\":\"Car 1 (REG 1)\",\"points\":[{\"start\":\"2024-11-05T00:00\"", json);
        Assert.Null(InsuranceCosts.ChartJson([newer, older], onlyVehicleId: 2));
    }

    [Fact]
    public void Policies_in_force_now_are_current_and_later_ones_upcoming()
    {
        var now = new DateTime(2026, 11, 5, 9, 0, 0);
        var ended = Policy(new DateTime(2025, 11, 5), new DateTime(2026, 11, 4, 23, 59, 0));
        var current = Policy(new DateTime(2026, 11, 5), new DateTime(2027, 11, 4, 23, 59, 0));
        var overlapping = Policy(new DateTime(2026, 10, 1), new DateTime(2026, 11, 5, 9, 0, 0));   // ended on the minute
        var upcoming = Policy(new DateTime(2027, 11, 5), new DateTime(2028, 11, 4, 23, 59, 0));
        InsurancePolicyView.MarkCurrent([ended, current, overlapping, upcoming], now);
        Assert.Equal([(false, false), (true, false), (false, false), (false, true)],
            new[] { ended, current, overlapping, upcoming }.Select(p => (p.IsCurrent, p.IsFuture)));
    }

    [Theory]
    [InlineData("2026-11-04T23:59", "2026-11-05T00:00", "2027-11-04T23:59")]   // the usual: midnight to 23:59 a year on
    [InlineData("2026-11-05T00:00", "2026-11-05T00:00", "2027-11-04T23:59")]   // one ending at midnight
    [InlineData("2026-11-05T14:32", "2026-11-05T00:00", "2027-11-04T23:59")]   // an odd time: overlapping, not a gap
    public void A_renewal_starts_when_the_last_policy_ends_and_runs_a_year(string previousEnd, string start, string end)
    {
        var renewal = CreateModel.RenewalStart(DateTime.Parse(previousEnd));
        Assert.Equal((DateTime.Parse(start), DateTime.Parse(end)), (renewal, CreateModel.DefaultEnd(renewal)));
    }
}
