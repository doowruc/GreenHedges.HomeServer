using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit;

public class TaxYearsTests
{
    [Theory]
    [InlineData("2026-04-05", 2025, 12)]   // the last day of 2025-26
    [InlineData("2026-04-06", 2026, 1)]    // the first of 2026-27
    [InlineData("2026-04-25", 2026, 1)]
    [InlineData("2026-05-05", 2026, 1)]    // months change on the 6th
    [InlineData("2026-05-06", 2026, 2)]
    [InlineData("2026-12-14", 2026, 9)]
    [InlineData("2027-01-25", 2026, 10)]
    [InlineData("2027-03-25", 2026, 12)]
    public void Dates_fall_in_tax_years_and_months(string date, int taxYear, int taxMonth)
    {
        var day = DateOnly.Parse(date);
        Assert.Equal((taxYear, taxMonth), (TaxYears.Of(day), TaxYears.MonthOf(day)));
    }

    [Fact]
    public void Tax_years_are_written_with_both_years() =>
        Assert.Equal(["1999-00", "2026-27"], new[] { TaxYears.Label(1999), TaxYears.Label(2026) });

    [Theory]
    [InlineData(1, "April")]
    [InlineData(9, "December")]
    [InlineData(10, "January")]
    [InlineData(12, "March")]
    public void Tax_months_are_named_by_their_calendar_month(int taxMonth, string name) =>
        Assert.Equal(name, TaxYears.MonthName(taxMonth));

    [Fact]
    public void Month_12_is_followed_by_month_1_of_the_next_year()
    {
        Assert.Equal((2026, 8), TaxYears.Next(2026, 7));
        Assert.Equal((2027, 1), TaxYears.Next(2026, 12));
    }

    [Fact]
    public void A_tax_year_runs_from_6_April_to_5_April() =>
        Assert.Equal((new DateOnly(2026, 4, 6), new DateOnly(2027, 4, 5)), (TaxYears.Start(2026), TaxYears.End(2026)));
}
