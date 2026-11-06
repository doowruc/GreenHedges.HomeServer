using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>
/// Matches a pension's contributions to its payslips (<see cref="ContributionMatching"/>) and sets those payslips'
/// pension received date. Only payslips going to the pension and without a received date take part, and only
/// contributions no payslip has been matched to yet (none of the pension's payslips was received that day).
/// </summary>
public class PensionPayslips(AppDbContext db)
{
    /// <summary>
    /// What matching would do, with <paramref name="pending"/> (contributions about to be imported) as well as those the
    /// pension has.
    /// </summary>
    public async Task<ContributionMatches> PlanAsync(int pensionId, IEnumerable<ReceivedContribution>? pending = null)
    {
        var payslips = await db.Payslips.Where(s => s.PensionId == pensionId)
            .Select(s => new
            {
                s.Id, s.TaxYear, s.TaxMonth, s.PayDate, s.PensionReceivedDate,
                Amount = s.Lines.Where(l => l.PayElement!.Category == PayCategory.Pension && l.PayElement.Kind != PayElementKind.Earning)
                    .Sum(l => l.Amount)
            })
            .ToListAsync();
        var received = payslips.Where(s => s.PensionReceivedDate is not null).Select(s => s.PensionReceivedDate!.Value).ToHashSet();

        var contributions = await db.PensionTransactions
            .Where(tx => tx.PensionId == pensionId && tx.Kind == TransactionKind.Contribution && tx.Value > 0)
            .Select(tx => new ReceivedContribution(tx.TradeDate, tx.Value))
            .ToListAsync();

        return ContributionMatching.Match(
            contributions.Concat(pending ?? []).Where(c => !received.Contains(c.Date)),
            payslips.Where(s => s.PensionReceivedDate is null).Select(s => new PayslipPension(s.Id, s.TaxYear, s.TaxMonth, s.PayDate, s.Amount)));
    }

    /// <summary>
    /// Sets the matched payslips' pension received dates, tracked so they save with the caller's other changes (and
    /// their row versions guard against a payslip edited meanwhile). Returns how many; the caller saves.
    /// </summary>
    public async Task<int> SetReceivedAsync(ContributionMatches plan)
    {
        var dates = plan.Matches.SelectMany(m => m.Payslips.Select(p => (p.PayslipId, m.Contribution.Date))).ToDictionary(x => x.PayslipId, x => x.Date);
        if (dates.Count == 0) return 0;
        var ids = dates.Keys.ToList();
        var payslips = await db.Payslips.Where(s => ids.Contains(s.Id) && s.PensionReceivedDate == null).ToListAsync();
        foreach (var payslip in payslips) payslip.PensionReceivedDate = dates[payslip.Id];
        return payslips.Count;
    }
}
