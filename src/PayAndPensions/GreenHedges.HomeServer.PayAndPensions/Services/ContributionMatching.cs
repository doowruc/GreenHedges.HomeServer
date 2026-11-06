namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>A payslip whose pension amounts haven't been seen arriving: its employee's and employer's pension together.</summary>
public sealed record PayslipPension(int PayslipId, int TaxYear, int TaxMonth, DateOnly PayDate, decimal Amount);

/// <summary>A contribution the pension received: a transaction's date and value.</summary>
public sealed record ReceivedContribution(DateOnly Date, decimal Value);

/// <summary>A contribution and the payslips whose pension amounts make it up, oldest first.</summary>
public sealed record ContributionMatch(ReceivedContribution Contribution, List<PayslipPension> Payslips);

/// <summary>What matching found: the matches, and the contributions no payslips add up to.</summary>
public sealed record ContributionMatches(List<ContributionMatch> Matches, List<ReceivedContribution> Unmatched);

/// <summary>
/// Matches the contributions a pension received to the payslips they came from, so each payslip's pension received
/// date can be set. A contribution is paid in after payday, for one month or several: it matches a run of payslips in
/// a row (among those not yet matched, by pay date) whose pension amounts, employee's and employer's, add up to it
/// exactly, paid in the <see cref="WindowDays"/> before it. The oldest such run wins, so money owed longest is
/// matched first. Pure, so unit tested.
/// </summary>
public static class ContributionMatching
{
    /// <summary>How long before a contribution its payslips can be: a few months' contributions paid in at once.</summary>
    public const int WindowDays = 120;

    /// <summary>The most payslips one contribution can be for.</summary>
    public const int MaxPayslips = 6;

    public static ContributionMatches Match(IEnumerable<ReceivedContribution> contributions, IEnumerable<PayslipPension> payslips)
    {
        var waiting = payslips.Where(p => p.Amount > 0).OrderBy(p => p.PayDate).ThenBy(p => p.TaxYear).ThenBy(p => p.TaxMonth).ToList();
        var matches = new List<ContributionMatch>();
        var unmatched = new List<ReceivedContribution>();

        foreach (var contribution in contributions.Where(c => c.Value > 0).OrderBy(c => c.Date))
        {
            var from = contribution.Date.AddDays(-WindowDays);
            var candidates = waiting.Where(p => p.PayDate >= from && p.PayDate <= contribution.Date).ToList();
            var run = FindRun(candidates, contribution.Value);
            if (run is null)
            {
                unmatched.Add(contribution);
                continue;
            }
            matches.Add(new ContributionMatch(contribution, run));
            waiting.RemoveAll(run.Contains);
        }
        return new ContributionMatches(matches, unmatched);
    }

    /// <summary>The oldest run of payslips in a row adding up to <paramref name="value"/>, or null.</summary>
    private static List<PayslipPension>? FindRun(List<PayslipPension> candidates, decimal value)
    {
        for (var start = 0; start < candidates.Count; start++)
        {
            var total = 0m;
            for (var end = start; end < candidates.Count && end - start < MaxPayslips; end++)
            {
                total += candidates[end].Amount;
                if (total == value) return candidates.GetRange(start, end - start + 1);
                if (total > value) break;
            }
        }
        return null;
    }
}
