using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>One payslip with its worked-out figures.</summary>
public sealed record PayslipSummary(int Id, int TaxYear, int TaxMonth, DateOnly PayDate, PayslipFigures Figures);

/// <summary>A person's whole pay history, worked out: every payslip oldest first, and every tax year's totals.</summary>
public sealed record PersonPay(IReadOnlyList<PayslipSummary> Payslips, IReadOnlyList<TaxYearTotals> TaxYears)
{
    public PayslipSummary? Latest => Payslips.Count > 0 ? Payslips[^1] : null;
}

/// <summary>Loads a person's payslips and works out their figures (<see cref="PayCalculations"/>), for the dashboard and
/// the tax years page. A person has a payslip a month, so all of them is a few hundred rows at most.</summary>
public sealed class PayHistory(AppDbContext db)
{
    public async Task<PersonPay> LoadAsync(int personId)
    {
        var payslips = await db.Payslips.Where(s => s.PersonId == personId)
            .OrderBy(s => s.TaxYear).ThenBy(s => s.TaxMonth)
            .Select(s => new
            {
                s.Id, s.TaxYear, s.TaxMonth, s.PayDate,
                Lines = s.Lines.Select(l => new { l.PayElement!.Kind, l.PayElement.Category, l.Amount }).ToList()
            })
            .ToListAsync();
        var amounts = payslips.Select(s => new PayslipAmounts(s.TaxYear, s.TaxMonth,
            s.Lines.Select(l => new PayLine(l.Kind, l.Category, l.Amount)).ToList())).ToList();
        var figures = PayCalculations.Calculate(amounts);
        return new PersonPay(
            payslips.Select((s, i) => new PayslipSummary(s.Id, s.TaxYear, s.TaxMonth, s.PayDate, figures[i])).ToList(),
            PayCalculations.TotalsByTaxYear(amounts));
    }
}
