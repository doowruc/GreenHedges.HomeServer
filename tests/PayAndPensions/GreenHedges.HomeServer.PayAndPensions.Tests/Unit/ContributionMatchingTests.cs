using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit;

/// <summary>Made-up payslips and contributions.</summary>
public class ContributionMatchingTests
{
    // A payslip a month, paid on the 28th, each with 300.00 of pension (employee's and employer's).
    private static PayslipPension Payslip(int taxMonth, decimal amount = 300m) =>
        new(taxMonth, 2020, taxMonth, new DateOnly(2020, 4, 28).AddMonths(taxMonth - 1), amount);

    private static ReceivedContribution Paid(int month, int day, decimal value) => new(new DateOnly(2020, month, day), value);

    /// <summary>Each match as "date: tax months".</summary>
    private static List<string> Matched(ContributionMatches result) =>
        result.Matches.Select(m => $"{m.Contribution.Date:dd/MM}: {string.Join(",", m.Payslips.Select(p => p.TaxMonth))}").ToList();

    [Fact]
    public void A_contribution_matches_one_months_or_several_months_pension_in_a_row()
    {
        var result = ContributionMatching.Match(
            [Paid(5, 21, 300m), Paid(7, 9, 600m), Paid(9, 9, 650m)],
            [Payslip(1), Payslip(2), Payslip(3), Payslip(4), Payslip(5, 350m)]);

        Assert.Equal(["21/05: 1", "09/07: 2,3", "09/09: 4,5"], Matched(result));
        Assert.Empty(result.Unmatched);
    }

    [Fact]
    public void The_oldest_payslips_are_matched_first()
    {
        var result = ContributionMatching.Match([Paid(7, 9, 300m), Paid(7, 30, 300m)], [Payslip(2), Payslip(3), Payslip(4)]);

        Assert.Equal(["09/07: 2", "30/07: 3"], Matched(result));
    }

    [Fact]
    public void Payslips_after_the_contribution_or_too_long_before_are_left_out()
    {
        var result = ContributionMatching.Match(
            [Paid(5, 1, 300m), Paid(12, 1, 300m)],
            [Payslip(1), Payslip(2)]);   // paid 28/04 and 28/05

        Assert.Equal(["01/05: 1"], Matched(result));
        Assert.Equal([Paid(12, 1, 300m)], result.Unmatched);   // 28/05 is more than 120 days before
    }

    [Fact]
    public void A_contribution_no_payslips_add_up_to_is_unmatched()
    {
        var result = ContributionMatching.Match([Paid(7, 9, 450m)], [Payslip(2), Payslip(3)]);

        Assert.Empty(result.Matches);
        Assert.Equal([Paid(7, 9, 450m)], result.Unmatched);
    }

    [Fact]
    public void Runs_are_of_payslips_in_a_row()
    {
        // 100 + 200 would be 300, but months 1 and 3 aren't next to each other.
        var result = ContributionMatching.Match([Paid(7, 9, 300m)], [Payslip(1, 100m), Payslip(2, 500m), Payslip(3, 200m)]);

        Assert.Empty(result.Matches);
    }
}
