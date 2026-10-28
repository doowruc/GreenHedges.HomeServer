using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.PayAndPensions.Data;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Services.Import;

/// <summary>What a workbook column becomes: its pay element's name, kind and category (as confirmed in the preview).</summary>
public sealed record ColumnChoice(int Index, string Name, PayElementKind Kind, PayCategory Category);

/// <summary>A column in the preview: what it would become, and whether that's one of the person's pay elements already.</summary>
/// <param name="ExistingId">The person's pay element with this name, whose kind and category are kept; null for a new one.</param>
public sealed record ColumnPlan(WorkbookColumn Column, ColumnChoice Choice, int? ExistingId);

/// <param name="ToImport">Rows for tax months the person has no payslip for yet.</param>
/// <param name="AlreadyThere">Rows for tax months that already have one, which are left as they are.</param>
/// <param name="Differences">Rows whose gross or net, worked out from the columns as chosen, isn't the sheet's own.</param>
public sealed record ImportPlan(
    IReadOnlyList<ColumnPlan> Columns,
    IReadOnlyList<WorkbookRow> ToImport,
    IReadOnlyList<WorkbookRow> AlreadyThere,
    IReadOnlyList<(WorkbookRow Row, string Difference)> Differences,
    IReadOnlyList<string> Problems);

public sealed record ImportResult(int Payslips, int NewPayElements, bool NewEmployer);

/// <summary>
/// Imports a person's payslip workbook (<see cref="PayslipWorkbook"/>): each pay column becomes a pay element (the
/// person's own of the same name if they have one), and each row a payslip from the chosen employer. Tax months the
/// person already has are left alone, so a workbook can be imported again after adding to it.
/// </summary>
public sealed class PayslipImporter(AppDbContext db, ILogger<PayslipImporter> logger)
{
    /// <summary>The messages are UK English whatever the machine's culture.</summary>
    private static readonly System.Globalization.CultureInfo UkCulture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");

    /// <summary>What importing <paramref name="contents"/> would do with <paramref name="choices"/> (the workbook's own
    /// suggestions if null), checked against the sheet's own totals. Saves nothing.</summary>
    public async Task<ImportPlan> PlanAsync(int personId, WorkbookContents contents, IReadOnlyList<ColumnChoice>? choices = null)
    {
        choices ??= contents.Columns.Select(c => new ColumnChoice(c.Index, c.Name, c.Kind, c.Category)).ToList();
        var problems = new List<string>(contents.Problems);
        if (choices.Count != contents.Columns.Count || choices.Select(c => c.Index).Order().Where((index, i) => index != i).Any())
            return new ImportPlan([], [], [], [], ["The columns don't match the workbook. Upload it again."]);

        var elements = await db.PayElements.Where(el => el.PersonId == personId)
            .Select(el => new { el.Id, el.Name, el.Kind, el.Category }).ToListAsync();
        var columns = choices.OrderBy(c => c.Index).Select(choice =>
        {
            var name = Pages.Names.Normalise(choice.Name);
            var existing = elements.FirstOrDefault(el => el.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var kept = existing is null ? choice with { Name = name } : choice with { Name = existing.Name, Kind = existing.Kind, Category = existing.Category };
            return new ColumnPlan(contents.Columns[choice.Index], kept, existing?.Id);
        }).ToList();

        foreach (var name in columns.Where(c => c.Choice.Name.Length is 0 or > 50).Select(c => c.Column.Header))
            problems.Add($"The {name} column needs a name of 1 to 50 characters.");
        foreach (var clash in columns.GroupBy(c => c.Choice.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"Columns {string.Join(" and ", clash.Select(c => c.Column.Letter))} would both be \"{clash.Key}\": give them different names.");

        var months = (await db.Payslips.Where(s => s.PersonId == personId).Select(s => new { s.TaxYear, s.TaxMonth }).ToListAsync())
            .Select(s => (s.TaxYear, s.TaxMonth)).ToHashSet();
        var toImport = contents.Rows.Where(r => !months.Contains((r.TaxYear, r.TaxMonth))).ToList();
        var alreadyThere = contents.Rows.Where(r => months.Contains((r.TaxYear, r.TaxMonth))).ToList();
        foreach (var repeated in toImport.GroupBy(r => (r.TaxYear, r.TaxMonth)).Where(g => g.Count() > 1))
            problems.Add($"Rows {string.Join(" and ", repeated.Select(r => r.RowNumber))} are both month {repeated.Key.TaxMonth} of {TaxYears.Label(repeated.Key.TaxYear)}.");

        return new ImportPlan(columns, toImport, alreadyThere, Check(contents.Rows, columns), problems);
    }

    /// <summary>
    /// Imports the plan's new rows: new pay elements for columns without one, a new employer if
    /// <paramref name="newEmployerName"/> is given (else <paramref name="employerId"/>, one of the person's), and the
    /// payslips. All in one save, so it's all or nothing. The plan must have no problems.
    /// </summary>
    public async Task<ImportResult> ImportAsync(int personId, ImportPlan plan, int? employerId, string? newEmployerName)
    {
        if (plan.Problems.Count > 0) throw new InvalidOperationException("The import plan has problems.");

        Employer employer;
        if (!string.IsNullOrWhiteSpace(newEmployerName))
        {
            employer = new Employer { PersonId = personId, Name = Pages.Names.Normalise(newEmployerName) };
            db.Employers.Add(employer);
        }
        else
        {
            employer = await db.Employers.FirstOrDefaultAsync(m => m.Id == employerId && m.PersonId == personId)
                ?? throw new InvalidOperationException("Not one of the person's employers.");
        }

        // New elements go after the person's others, in the workbook's order.
        var order = await db.PayElements.Where(el => el.PersonId == personId).MaxAsync(el => (int?)el.SortOrder) ?? 0;
        var elements = new Dictionary<int, PayElement>();
        foreach (var column in plan.Columns)
        {
            if (column.ExistingId is { } id)
            {
                elements[column.Column.Index] = await db.PayElements.FirstAsync(el => el.Id == id);
                continue;
            }
            var element = new PayElement
            {
                PersonId = personId, Name = column.Choice.Name, Kind = column.Choice.Kind, Category = column.Choice.Category,
                SortOrder = order += 10
            };
            db.PayElements.Add(element);
            elements[column.Column.Index] = element;
        }

        foreach (var row in plan.ToImport)
            db.Payslips.Add(new Payslip
            {
                PersonId = personId, Employer = employer, TaxYear = row.TaxYear, TaxMonth = row.TaxMonth, PayDate = row.PayDate,
                PensionReceivedDate = row.PensionReceivedDate, Notes = row.Notes is { Length: > 500 } n ? n[..500] : row.Notes,
                Lines = row.Amounts.Select(a => new PayslipLine { PayElement = elements[a.Key], Amount = a.Value }).ToList()
            });

        await db.SaveChangesAsync();
        var result = new ImportResult(plan.ToImport.Count, plan.Columns.Count(c => c.ExistingId is null), !string.IsNullOrWhiteSpace(newEmployerName));
        // Counts only: never names or amounts.
        logger.LogInformation("Imported {Payslips} payslips and {PayElements} new pay elements for person {PersonId}",
            result.Payslips, result.NewPayElements, personId);
        return result;
    }

    /// <summary>Rows whose gross or net, from the columns' kinds, differs from the sheet's own by more than a penny's rounding.</summary>
    private static List<(WorkbookRow, string)> Check(IEnumerable<WorkbookRow> rows, IReadOnlyList<ColumnPlan> columns)
    {
        var kinds = columns.ToDictionary(c => c.Column.Index, c => c.Choice.Kind);
        var differences = new List<(WorkbookRow, string)>();
        foreach (var row in rows)
        {
            var gross = row.Amounts.Where(a => kinds[a.Key] == PayElementKind.Earning).Sum(a => a.Value);
            var net = gross - row.Amounts.Where(a => kinds[a.Key] == PayElementKind.Deduction).Sum(a => a.Value);
            if (row.SheetGross is { } g && Math.Abs(g - gross) > 0.005m)
                differences.Add((row, string.Create(UkCulture, $"gross {gross:N2}, the sheet's {g:N2}")));
            else if (row.SheetNet is { } n && Math.Abs(n - net) > 0.005m)
                differences.Add((row, string.Create(UkCulture, $"net {net:N2}, the sheet's {n:N2}")));
        }
        return differences;
    }
}
