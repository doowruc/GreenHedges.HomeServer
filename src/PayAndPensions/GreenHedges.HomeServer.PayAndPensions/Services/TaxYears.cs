using System.Globalization;

namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>
/// UK tax years: 6 April to 5 April. A tax year is named by the year it starts in (2026 is 2026-27), and its months
/// run from 1 (6 April to 5 May) to 12 (6 March to 5 April).
/// </summary>
public static class TaxYears
{
    public static DateOnly Start(int taxYear) => new(taxYear, 4, 6);

    public static DateOnly End(int taxYear) => new(taxYear + 1, 4, 5);

    /// <summary>How tax years are written, e.g. "2026-27".</summary>
    public static string Label(int taxYear) => $"{taxYear}-{(taxYear + 1) % 100:00}";

    /// <summary>The tax year a date falls in.</summary>
    public static int Of(DateOnly date) => date < Start(date.Year) ? date.Year - 1 : date.Year;

    /// <summary>The tax month a date falls in, 1 to 12.</summary>
    public static int MonthOf(DateOnly date)
    {
        // Months change on the 6th: 5 May is still month 1.
        var calendarMonth = date.Day >= 6 ? date.Month : date.AddMonths(-1).Month;
        return (calendarMonth + 8) % 12 + 1;
    }

    /// <summary>The calendar month a tax month mostly falls in, e.g. "April" for month 1.</summary>
    public static string MonthName(int taxMonth) =>
        CultureInfo.GetCultureInfo("en-GB").DateTimeFormat.GetMonthName((taxMonth + 2) % 12 + 1);

    /// <summary>The tax month after <paramref name="taxMonth"/> of <paramref name="taxYear"/>: month 12 is followed by month 1 of the next year.</summary>
    public static (int TaxYear, int TaxMonth) Next(int taxYear, int taxMonth) =>
        taxMonth == 12 ? (taxYear + 1, 1) : (taxYear, taxMonth + 1);
}
