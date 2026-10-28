using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Pages.Payslips;

/// <summary>
/// A person's payslips for one tax year, laid out like their spreadsheet: a column per pay element used that year, then
/// the worked-out figures, and the year's totals.
/// </summary>
public class IndexModel(AppDbContext db) : PersonPageModel(db)
{
    public record Column(int Id, string Name, PayElementKind Kind);

    public record Row(int Id, int TaxMonth, DateOnly PayDate, string Employer, DateOnly? PensionReceivedDate, string? Notes,
        IReadOnlyDictionary<int, decimal> Amounts, PayslipFigures Figures);

    /// <summary>The tax year shown: the one asked for, else the person's latest, else this one.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    /// <summary>Tax years with payslips, latest first (and the one shown, if it has none).</summary>
    public List<int> Years { get; private set; } = [];

    public List<Column> Columns { get; private set; } = [];

    public List<Row> Rows { get; private set; } = [];

    public TaxYearTotals? Totals { get; private set; }

    /// <summary>Whether the payslips are from more than one employer, so it's worth a column.</summary>
    public bool ManyEmployers { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadPersonAsync()) return NotFound();

        Years = await Db.Payslips.Where(s => s.PersonId == PersonId).Select(s => s.TaxYear).Distinct()
            .OrderByDescending(y => y).ToListAsync();
        Year ??= Years.Count > 0 ? Years[0] : TaxYears.Of(DateOnly.FromDateTime(DateTime.Today));
        if (!Years.Contains(Year.Value)) Years = Years.Append(Year.Value).OrderDescending().ToList();

        // The year before comes too: its last payslip for the first rise, and all of it for the totals' change.
        var payslips = await Db.Payslips
            .Where(s => s.PersonId == PersonId && s.TaxYear <= Year && s.TaxYear >= Year - 1)
            .OrderBy(s => s.TaxYear).ThenBy(s => s.TaxMonth)
            .Select(s => new
            {
                s.Id, s.TaxYear, s.TaxMonth, s.PayDate, Employer = s.Employer!.Name, s.PensionReceivedDate, s.Notes,
                Lines = s.Lines.Select(l => new
                {
                    l.PayElementId, l.PayElement!.Name, l.PayElement.Kind, l.PayElement.Category, l.PayElement.SortOrder, l.Amount
                }).ToList()
            })
            .ToListAsync();
        var before = payslips.LastOrDefault(s => s.TaxYear < Year);
        var shown = payslips.Where(s => s.TaxYear == Year).ToList();
        var calculated = (before is null ? shown : shown.Prepend(before)).ToList();
        var figures = PayCalculations.Calculate(calculated
            .Select(s => new PayslipAmounts(s.TaxYear, s.TaxMonth, s.Lines.Select(l => new PayLine(l.Kind, l.Category, l.Amount)).ToList()))
            .ToList());
        if (before is not null) figures = figures.Skip(1).ToList();

        Rows = shown.Select((s, i) => new Row(s.Id, s.TaxMonth, s.PayDate, s.Employer, s.PensionReceivedDate, s.Notes,
            s.Lines.ToDictionary(l => l.PayElementId, l => l.Amount), figures[i])).ToList();
        Columns = shown.SelectMany(s => s.Lines)
            .DistinctBy(l => l.PayElementId)
            .OrderBy(l => l.Kind).ThenBy(l => l.SortOrder).ThenBy(l => l.Name)
            .Select(l => new Column(l.PayElementId, l.Name, l.Kind)).ToList();
        ManyEmployers = shown.Select(s => s.Employer).Distinct().Count() > 1;

        // The totals compare with the year before, whose payslips are all here too.
        Totals = PayCalculations.TotalsByTaxYear(payslips
                .Select(s => new PayslipAmounts(s.TaxYear, s.TaxMonth, s.Lines.Select(l => new PayLine(l.Kind, l.Category, l.Amount)).ToList())))
            .FirstOrDefault(t => t.TaxYear == Year);
        return Page();
    }
}
