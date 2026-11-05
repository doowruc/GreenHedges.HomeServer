using GreenHedges.HomeServer.VehicleManager.Pages.Vehicles.Insurance;
using GreenHedges.HomeServer.VehicleManager.ViewModels;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class InsurancePolicyViewTests
{
    private static InsurancePolicyView Policy(DateTime start, DateTime end, decimal premium = 400m) =>
        new() { StartDate = start, EndDate = end, Premium = premium };

    [Fact]
    public void The_renewal_date_is_the_last_day_covered()
    {
        Assert.Equal(new DateOnly(2026, 11, 4), InsurancePolicyView.LastDay(new DateTime(2026, 11, 4, 23, 59, 0)));
        Assert.Equal(new DateOnly(2026, 11, 5), InsurancePolicyView.LastDay(new DateTime(2026, 11, 5, 14, 32, 0)));
        // Midnight is the start of the 5th, so the 4th was the last day.
        Assert.Equal(new DateOnly(2026, 11, 4), InsurancePolicyView.LastDay(new DateTime(2026, 11, 5, 0, 0, 0)));
    }

    [Fact]
    public void A_years_policy_costs_its_premium_a_year()
    {
        var policy = Policy(new DateTime(2025, 11, 5), new DateTime(2026, 11, 4, 23, 59, 0), 450m);
        Assert.False(policy.IsPartYear);
        Assert.Equal(450m, Math.Round(policy.YearlyPremium));
    }

    [Fact]
    public void A_short_policys_premium_is_scaled_up_to_a_year()
    {
        var policy = Policy(new DateTime(2026, 5, 6), new DateTime(2026, 11, 4, 23, 59, 0), 250m);   // about half a year
        Assert.True(policy.IsPartYear);
        Assert.InRange(policy.YearlyPremium, 495m, 505m);
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
    [InlineData("2026-11-05T14:32", "2026-11-05T14:32", "2027-11-05T14:32")]   // to the minute it started
    public void A_renewal_starts_when_the_last_policy_ends_and_runs_a_year(string previousEnd, string start, string end)
    {
        var renewal = CreateModel.RenewalStart(DateTime.Parse(previousEnd));
        Assert.Equal((DateTime.Parse(start), DateTime.Parse(end)), (renewal, CreateModel.DefaultEnd(renewal)));
    }
}
