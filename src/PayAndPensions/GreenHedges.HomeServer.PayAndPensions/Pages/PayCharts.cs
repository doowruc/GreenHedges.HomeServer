using System.Text.Json;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages;

/// <summary>The data for the pay charts (wwwroot/js/pay-charts.js), as JSON for a canvas's data attribute.</summary>
public static class PayCharts
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Gross, net and employee pension per tax year, oldest first.</summary>
    public static string Yearly(IEnumerable<TaxYearTotals> years)
    {
        var list = years.ToList();
        return JsonSerializer.Serialize(new
        {
            labels = list.Select(t => TaxYears.Label(t.TaxYear)),
            gross = list.Select(t => t.Gross),
            net = list.Select(t => t.Net),
            pension = list.Select(t => t.EmployeePension)
        }, Json);
    }

    /// <summary>Gross and net per payslip, oldest first, labelled by pay date (e.g. "Apr 26").</summary>
    public static string Monthly(IEnumerable<PayslipSummary> payslips)
    {
        var list = payslips.ToList();
        return JsonSerializer.Serialize(new
        {
            labels = list.Select(s => s.PayDate.ToString("MMM yy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"))),
            gross = list.Select(s => s.Figures.Gross),
            net = list.Select(s => s.Figures.Net)
        }, Json);
    }
}
