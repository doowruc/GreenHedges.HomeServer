using GreenHedges.HomeServer.VehicleManager.Services;

namespace GreenHedges.HomeServer.VehicleManager.Tests.Unit;

public class PreRenewalTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 9, 30, 0);

    [Fact]
    public void The_renewal_is_when_the_current_policy_finishes()
    {
        var ended = (new DateTime(2024, 12, 1), new DateTime(2025, 11, 30, 23, 59, 0));
        var current = (new DateTime(2025, 12, 1), new DateTime(2026, 11, 30, 23, 59, 0));
        var upcoming = (new DateTime(2026, 12, 1), new DateTime(2027, 11, 30, 23, 59, 0));
        Assert.Equal(new DateOnly(2026, 12, 1), PreRenewal.RenewalDate([ended, current, upcoming], Now));
        // One ending at midnight renews that day.
        Assert.Equal(new DateOnly(2026, 12, 1), PreRenewal.RenewalDate([(new DateTime(2025, 12, 1), new DateTime(2026, 12, 1))], Now));
        Assert.Null(PreRenewal.RenewalDate([ended, upcoming], Now));
    }

    [Theory]
    [InlineData("2023-12-02", true, true, true)]     // just inside 3 years
    [InlineData("2023-12-01", false, true, true)]    // exactly 3 years before: outside "the last 3 years"
    [InlineData("2022-06-15", false, false, true)]
    [InlineData("2021-12-01", false, false, false)]  // exactly 5 years before
    [InlineData("2026-12-02", false, false, false)]  // after the renewal
    public void A_claim_is_declared_within_each_cut_off_before_the_renewal(string date, bool three, bool four, bool five)
    {
        var renewal = new DateOnly(2026, 12, 1);
        Assert.Equal([three, four, five], PreRenewal.CutOffYears.Select(y => PreRenewal.MustDeclare(DateOnly.Parse(date), renewal, y)));
    }

    [Theory]
    [InlineData("2014-12-01", 12)]   // the anniversary is the renewal day
    [InlineData("2014-12-02", 11)]
    [InlineData("2016-02-29", 10)]
    public void Licence_years_are_whole_years_by_the_renewal(string issued, int years) =>
        Assert.Equal(years, PreRenewal.FullYears(DateOnly.Parse(issued), new DateOnly(2026, 12, 1)));

    [Fact]
    public void A_driver_number_is_split_into_its_sections()
    {
        Assert.Equal([("Surname", "MORGA"), ("Date of birth", "753116"), ("Initials", "SM"), ("Digit", "9"), ("Check letters", "IJ")],
            PreRenewal.Sections("MORGA753116SM9IJ"));
        Assert.Equal("MORGA 753116 SM9IJ", PreRenewal.Grouped("MORGA753116SM9IJ"));
        Assert.Null(PreRenewal.Sections(null));
    }
}
