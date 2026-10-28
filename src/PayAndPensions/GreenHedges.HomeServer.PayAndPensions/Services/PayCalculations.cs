using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>One amount on a payslip, with what its pay element is.</summary>
public sealed record PayLine(PayElementKind Kind, PayCategory Category, decimal Amount);

/// <summary>What the calculations need from a payslip.</summary>
public sealed record PayslipAmounts(int TaxYear, int TaxMonth, IReadOnlyList<PayLine> Lines);

/// <summary>
/// The figures worked out from one payslip and those before it, as the spreadsheets did. Percentages are fractions
/// (0.25 is 25%), and null where they can't be worked out (nothing to divide by, or nothing before to compare with).
/// </summary>
/// <param name="Gross">Earnings.</param>
/// <param name="Deductions">What comes off gross: tax, NI, employee pension, other.</param>
/// <param name="Net">Take-home pay: gross less deductions.</param>
/// <param name="EmployerContributions">Shown on the payslip but in neither gross nor net, e.g. employer pension.</param>
/// <param name="Salary">The month's earnings in the Salary category.</param>
/// <param name="AnnualSalary">Salary × 12.</param>
/// <param name="Rise">How much the annual salary changed since the payslip before; null if it didn't, or this is the first.</param>
/// <param name="RisePercent">The rise as a fraction of the annual salary before it.</param>
/// <param name="EmployeePensionPercent">Employee pension deductions as a fraction of salary.</param>
/// <param name="EmployerPensionPercent">Employer pension contributions as a fraction of salary.</param>
/// <param name="TakeHomePercent">Net as a fraction of gross.</param>
/// <param name="YearToDateGross">Gross for the tax year so far, this payslip included.</param>
/// <param name="PredictedAnnualGross">The tax year's gross if the rest of it goes as it has so far: 12 ÷ tax month × year to date.</param>
/// <param name="BonusPercent">Bonus earnings as a fraction of the annual salary; null without a bonus.</param>
public sealed record PayslipFigures(
    decimal Gross,
    decimal Deductions,
    decimal Net,
    decimal EmployerContributions,
    decimal Salary,
    decimal AnnualSalary,
    decimal? Rise,
    decimal? RisePercent,
    decimal? EmployeePensionPercent,
    decimal? EmployerPensionPercent,
    decimal? TakeHomePercent,
    decimal YearToDateGross,
    decimal PredictedAnnualGross,
    decimal? BonusPercent);

/// <summary>A tax year's totals, compared with the year before where there is one.</summary>
/// <param name="EmployeePension">Employee pension deductions.</param>
/// <param name="PensionRelief">The extra pension tax relief a higher-rate taxpayer reclaims through Self Assessment: 20% of
/// <paramref name="EmployeePension"/> (relief-at-source contributions, which only get basic-rate relief at source).</param>
/// <param name="GrossChange">Gross less the same tax months' of the year before; null without a year before.</param>
/// <param name="NetChange">Net less the same tax months' of the year before; null without a year before.</param>
public sealed record TaxYearTotals(
    int TaxYear,
    int Payslips,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    decimal EmployerContributions,
    decimal Tax,
    decimal NationalInsurance,
    decimal EmployeePension,
    decimal EmployerPension,
    decimal PensionRelief,
    decimal? TakeHomePercent,
    decimal? GrossChange,
    decimal? GrossChangePercent,
    decimal? NetChange,
    decimal? NetChangePercent);

public static class PayCalculations
{
    /// <summary>The higher rate less the basic rate: what a higher-rate taxpayer reclaims on relief-at-source pension contributions.</summary>
    public const decimal PensionReliefRate = 0.20m;

    /// <summary>
    /// Works out each payslip's figures. <paramref name="payslips"/> must be one person's, oldest first (rises and the
    /// year to date depend on the ones before). Returns the figures in the same order.
    /// </summary>
    public static IReadOnlyList<PayslipFigures> Calculate(IReadOnlyList<PayslipAmounts> payslips)
    {
        var figures = new List<PayslipFigures>(payslips.Count);
        decimal? previousAnnualSalary = null;
        (int TaxYear, decimal Gross) yearToDate = (int.MinValue, 0m);

        foreach (var payslip in payslips)
        {
            decimal Sum(PayElementKind kind, PayCategory? category = null) =>
                payslip.Lines.Where(l => l.Kind == kind && (category is null || l.Category == category)).Sum(l => l.Amount);

            var gross = Sum(PayElementKind.Earning);
            var deductions = Sum(PayElementKind.Deduction);
            var salary = Sum(PayElementKind.Earning, PayCategory.Salary);
            var annualSalary = salary * 12;
            var bonus = Sum(PayElementKind.Earning, PayCategory.Bonus);

            decimal? rise = previousAnnualSalary is { } before && annualSalary != before ? annualSalary - before : null;
            decimal? risePercent = rise is { } r && previousAnnualSalary is { } b && b != 0 ? r / b : null;
            previousAnnualSalary = annualSalary;

            yearToDate = yearToDate.TaxYear == payslip.TaxYear ? (payslip.TaxYear, yearToDate.Gross + gross) : (payslip.TaxYear, gross);

            figures.Add(new PayslipFigures(
                Gross: gross,
                Deductions: deductions,
                Net: gross - deductions,
                EmployerContributions: Sum(PayElementKind.EmployerContribution),
                Salary: salary,
                AnnualSalary: annualSalary,
                Rise: rise,
                RisePercent: risePercent,
                EmployeePensionPercent: Fraction(Sum(PayElementKind.Deduction, PayCategory.Pension), salary),
                EmployerPensionPercent: Fraction(Sum(PayElementKind.EmployerContribution, PayCategory.Pension), salary),
                TakeHomePercent: Fraction(gross - deductions, gross),
                YearToDateGross: yearToDate.Gross,
                PredictedAnnualGross: 12m / payslip.TaxMonth * yearToDate.Gross,
                BonusPercent: bonus == 0 ? null : Fraction(bonus, annualSalary)));
        }

        return figures;
    }

    /// <summary>
    /// Totals for each tax year in <paramref name="payslips"/> (one person's, any order), oldest first. Each is compared
    /// with the same tax months of the year just before, so a year in progress is compared like for like (and a whole
    /// year with the whole year before).
    /// </summary>
    public static IReadOnlyList<TaxYearTotals> TotalsByTaxYear(IEnumerable<PayslipAmounts> payslips)
    {
        var years = payslips.GroupBy(p => p.TaxYear).OrderBy(g => g.Key).ToList();
        var byYear = years.ToDictionary(g => g.Key, g => g.ToList());
        var totals = new List<TaxYearTotals>();
        foreach (var year in years)
        {
            var lines = year.SelectMany(p => p.Lines).ToList();
            decimal Sum(PayElementKind kind, PayCategory? category = null) => SumOf(lines, kind, category);

            var gross = Sum(PayElementKind.Earning);
            var deductions = Sum(PayElementKind.Deduction);
            var net = gross - deductions;
            var employeePension = Sum(PayElementKind.Deduction, PayCategory.Pension);

            // Only against the year just before (a gap of a year would make the change meaningless), and only its
            // months this year has too.
            decimal? grossBefore = null, netBefore = null;
            if (byYear.TryGetValue(year.Key - 1, out var previous))
            {
                var months = year.Select(p => p.TaxMonth).ToHashSet();
                var linesBefore = previous.Where(p => months.Contains(p.TaxMonth)).SelectMany(p => p.Lines).ToList();
                grossBefore = SumOf(linesBefore, PayElementKind.Earning);
                netBefore = grossBefore - SumOf(linesBefore, PayElementKind.Deduction);
            }

            totals.Add(new TaxYearTotals(
                TaxYear: year.Key,
                Payslips: year.Count(),
                Gross: gross,
                Deductions: deductions,
                Net: net,
                EmployerContributions: Sum(PayElementKind.EmployerContribution),
                Tax: Sum(PayElementKind.Deduction, PayCategory.Tax),
                NationalInsurance: Sum(PayElementKind.Deduction, PayCategory.NationalInsurance),
                EmployeePension: employeePension,
                EmployerPension: Sum(PayElementKind.EmployerContribution, PayCategory.Pension),
                PensionRelief: Math.Round(employeePension * PensionReliefRate, 2),
                TakeHomePercent: Fraction(net, gross),
                GrossChange: gross - grossBefore,
                GrossChangePercent: grossBefore is { } gb ? Fraction(gross - gb, gb) : null,
                NetChange: net - netBefore,
                NetChangePercent: netBefore is { } nb ? Fraction(net - nb, nb) : null));
        }
        return totals;
    }

    private static decimal SumOf(IEnumerable<PayLine> lines, PayElementKind kind, PayCategory? category = null) =>
        lines.Where(l => l.Kind == kind && (category is null || l.Category == category)).Sum(l => l.Amount);

    private static decimal? Fraction(decimal part, decimal whole) => whole == 0 ? null : part / whole;
}
