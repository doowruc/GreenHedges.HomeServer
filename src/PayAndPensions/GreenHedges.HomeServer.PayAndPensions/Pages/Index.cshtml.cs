using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages;

/// <summary>
/// The dashboard, one person at a time: their latest payslip, the tax year so far, and charts of their pay by month and
/// by tax year. A person without payslips gets the links to set them up instead.
/// </summary>
public class IndexModel(AppDbContext db, PayHistory history) : PageModel
{
    public record PersonOption(int Id, string Name, int Employers, int PayElements, int Payslips);

    /// <param name="Net">Net for the tax year so far.</param>
    public record YearSoFar(int TaxYear, int Payslips, decimal Gross, decimal Net, decimal PredictedGross);

    /// <summary>The person shown: the one asked for, else the first with payslips, else the first.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Person { get; set; }

    public List<PersonOption> People { get; private set; } = [];

    public PersonOption? Shown { get; private set; }

    public PayslipSummary? Latest { get; private set; }

    /// <summary>The latest payslip's tax year, so far.</summary>
    public YearSoFar? ThisYear { get; private set; }

    /// <summary>The tax year before it, in full, with its change on the one before.</summary>
    public TaxYearTotals? LastYear { get; private set; }

    public string MonthlyChartJson { get; private set; } = "{}";

    public string YearlyChartJson { get; private set; } = "{}";

    public async Task<IActionResult> OnGetAsync()
    {
        People = await db.People.OrderBy(p => p.Name)
            .Select(p => new PersonOption(p.Id, p.Name, p.Employers.Count, p.PayElements.Count, p.Payslips.Count))
            .ToListAsync();
        if (People.Count == 0) return Page();

        Shown = Person is { } id
            ? People.FirstOrDefault(p => p.Id == id)
            : People.FirstOrDefault(p => p.Payslips > 0) ?? People[0];
        if (Shown is null) return NotFound();
        if (Shown.Payslips == 0) return Page();

        var pay = await history.LoadAsync(Shown.Id);
        Latest = pay.Latest!;
        var thisYear = pay.Payslips.Where(s => s.TaxYear == Latest.TaxYear).ToList();
        ThisYear = new YearSoFar(Latest.TaxYear, thisYear.Count, Latest.Figures.YearToDateGross, thisYear.Sum(s => s.Figures.Net),
            Latest.Figures.PredictedAnnualGross);
        LastYear = pay.TaxYears.FirstOrDefault(t => t.TaxYear == Latest.TaxYear - 1);
        MonthlyChartJson = PayCharts.Monthly(pay.Payslips);
        YearlyChartJson = PayCharts.Yearly(pay.TaxYears);
        return Page();
    }
}
