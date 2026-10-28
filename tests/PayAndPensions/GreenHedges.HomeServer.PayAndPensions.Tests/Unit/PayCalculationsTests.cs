using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit;

/// <summary>Made-up payslips: a salary of 3,000 a month, rising to 3,300.</summary>
public class PayCalculationsTests
{
    private static PayLine Salary(decimal amount) => new(PayElementKind.Earning, PayCategory.Salary, amount);
    private static PayLine Overtime(decimal amount) => new(PayElementKind.Earning, PayCategory.Overtime, amount);
    private static PayLine Bonus(decimal amount) => new(PayElementKind.Earning, PayCategory.Bonus, amount);
    private static PayLine Tax(decimal amount) => new(PayElementKind.Deduction, PayCategory.Tax, amount);
    private static PayLine NI(decimal amount) => new(PayElementKind.Deduction, PayCategory.NationalInsurance, amount);
    private static PayLine Pension(decimal amount) => new(PayElementKind.Deduction, PayCategory.Pension, amount);
    private static PayLine EmployerPension(decimal amount) => new(PayElementKind.EmployerContribution, PayCategory.Pension, amount);

    private static PayslipAmounts Payslip(int taxYear, int taxMonth, params PayLine[] lines) => new(taxYear, taxMonth, lines);

    [Fact]
    public void Gross_deductions_and_net_leave_out_the_employers_contributions()
    {
        var f = PayCalculations.Calculate([Payslip(2026, 1, Salary(3000), Overtime(250), Tax(500), NI(200), Pension(150), EmployerPension(225))])[0];

        Assert.Equal((3250m, 850m, 2400m, 225m), (f.Gross, f.Deductions, f.Net, f.EmployerContributions));
        Assert.Equal(36000m, f.AnnualSalary);
        Assert.Equal(0.05m, f.EmployeePensionPercent);      // of salary, not gross
        Assert.Equal(0.075m, f.EmployerPensionPercent);
        Assert.Equal(2400m / 3250m, f.TakeHomePercent);
    }

    [Fact]
    public void A_rise_is_shown_only_when_the_annual_salary_changes()
    {
        var f = PayCalculations.Calculate(
        [
            Payslip(2026, 1, Salary(3000)),
            Payslip(2026, 2, Salary(3000)),
            Payslip(2026, 3, Salary(3300)),
        ]);

        Assert.Null(f[0].Rise);      // nothing before it
        Assert.Null(f[1].Rise);      // unchanged
        Assert.Equal(3600m, f[2].Rise);
        Assert.Equal(0.1m, f[2].RisePercent);
    }

    [Fact]
    public void The_year_to_date_starts_again_each_tax_year_and_predicts_the_year()
    {
        var f = PayCalculations.Calculate(
        [
            Payslip(2025, 12, Salary(3000)),
            Payslip(2026, 1, Salary(3000)),
            Payslip(2026, 2, Salary(3000), Overtime(600)),
            Payslip(2026, 3, Salary(3000)),
        ]);

        Assert.Equal([3000m, 3000m, 6600m, 9600m], f.Select(x => x.YearToDateGross));
        Assert.Equal(3000m, f[0].PredictedAnnualGross);         // month 12, but only recorded payslips count (as the spreadsheet)
        Assert.Equal(39600m, f[2].PredictedAnnualGross);        // 12 ÷ 2 × 6,600
        Assert.Equal(38400m, f[3].PredictedAnnualGross);
    }

    [Fact]
    public void A_bonus_is_a_fraction_of_the_annual_salary()
    {
        var f = PayCalculations.Calculate([Payslip(2026, 9, Salary(3000), Bonus(3600))])[0];
        Assert.Equal(0.1m, f.BonusPercent);
        Assert.Null(PayCalculations.Calculate([Payslip(2026, 9, Salary(3000))])[0].BonusPercent);
    }

    [Fact]
    public void Nothing_is_divided_by_zero()
    {
        var f = PayCalculations.Calculate([Payslip(2026, 1, Overtime(0))])[0];
        Assert.Equal((0m, 0m), (f.Gross, f.Net));
        Assert.Null(f.TakeHomePercent);
        Assert.Null(f.EmployeePensionPercent);
        Assert.Null(f.EmployerPensionPercent);
    }

    [Fact]
    public void Negative_amounts_count_against_their_kind()
    {
        var backPayTakenBack = new PayLine(PayElementKind.Earning, PayCategory.BackPay, -100);
        var f = PayCalculations.Calculate([Payslip(2026, 1, Salary(3000), backPayTakenBack, Tax(400))])[0];
        Assert.Equal((2900m, 2500m), (f.Gross, f.Net));
    }

    [Fact]
    public void Tax_year_totals_add_up_and_compare_with_the_year_before()
    {
        var payslips = new List<PayslipAmounts>();
        for (var month = 1; month <= 12; month++)
        {
            payslips.Add(Payslip(2025, month, Salary(3000), Tax(500), NI(200), Pension(150), EmployerPension(225)));
            payslips.Add(Payslip(2026, month, Salary(3300), Tax(560), NI(220), Pension(165), EmployerPension(250)));
        }

        var totals = PayCalculations.TotalsByTaxYear(payslips);

        Assert.Equal([2025, 2026], totals.Select(t => t.TaxYear));
        var (first, second) = (totals[0], totals[1]);
        Assert.Equal((12, 36000m, 10200m, 25800m), (first.Payslips, first.Gross, first.Deductions, first.Net));
        Assert.Equal((6000m, 2400m, 1800m, 2700m), (first.Tax, first.NationalInsurance, first.EmployeePension, first.EmployerPension));
        Assert.Equal(360m, first.PensionRelief);                // 20% of 1,800
        Assert.Null(first.GrossChange);
        Assert.Equal(3600m, second.GrossChange);
        Assert.Equal(0.1m, second.GrossChangePercent);
        Assert.Equal(28260m - 25800m, second.NetChange);
        Assert.Equal(25800m / 36000m, first.TakeHomePercent);
    }

    [Fact]
    public void A_year_isnt_compared_across_a_gap()
    {
        var totals = PayCalculations.TotalsByTaxYear([Payslip(2023, 1, Salary(3000)), Payslip(2025, 1, Salary(3000))]);
        Assert.Null(totals[1].GrossChange);
    }
}
