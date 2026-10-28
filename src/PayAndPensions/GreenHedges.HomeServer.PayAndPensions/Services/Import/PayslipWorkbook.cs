using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Services.Import;

/// <summary>A column of the workbook that becomes a pay element.</summary>
/// <param name="Index">Its position among the pay columns, from 0: how the preview and the import refer to it.</param>
/// <param name="Header">The heading as written.</param>
/// <param name="Name">The pay element's name: the heading, made unique if two columns share it.</param>
/// <param name="Kind">From the sheet's Gross and Deductions formulas where it has them.</param>
/// <param name="Category">A guess from the heading, for the preview to confirm.</param>
public sealed record WorkbookColumn(int Index, string Letter, string Header, string Name, PayElementKind Kind, PayCategory Category);

/// <summary>One payslip row.</summary>
/// <param name="Amounts">Non-zero amounts by <see cref="WorkbookColumn.Index"/>, to the penny.</param>
/// <param name="SheetGross">The sheet's own Gross for the row, to check the import against; null without the column.</param>
/// <param name="SheetNet">The sheet's own Net for the row; null without the column.</param>
public sealed record WorkbookRow(
    int RowNumber,
    int TaxYear,
    int TaxMonth,
    DateOnly PayDate,
    DateOnly? PensionReceivedDate,
    string? Notes,
    IReadOnlyDictionary<int, decimal> Amounts,
    decimal? SheetGross,
    decimal? SheetNet);

/// <param name="Problems">What stops the workbook being read at all; empty if it was.</param>
/// <param name="KindsFromFormulas">Whether the kinds came from the Gross and Deductions formulas (else from where Tax is).</param>
/// <param name="SkippedRows">Rows with a date that can't be imported, with why (by row number).</param>
public sealed record WorkbookContents(
    IReadOnlyList<WorkbookColumn> Columns,
    IReadOnlyList<WorkbookRow> Rows,
    IReadOnlyList<string> Problems,
    bool KindsFromFormulas,
    IReadOnlyList<(int RowNumber, string Reason)> SkippedRows);

/// <summary>
/// Reads a payslip spreadsheet, as kept before this app: one sheet, a heading row with Year (the year the tax year ends),
/// Period (the tax month) and Date (the pay date), then a column per kind of pay up to Gross; Gross, Deductions, Net and
/// the other worked-out columns after it are ignored, except as a check. Optional columns anywhere: Transfer Date (when
/// the pension contributions arrived) and Notes (text only). Rows without a date are skipped (future months laid out in
/// advance).
/// </summary>
/// <remarks>
/// Which columns are earnings and which deductions comes from the first payslip row's Gross and Deductions formulas
/// (<c>=SUM(D2:N2)</c>, <c>=SUM(O2:R2)</c>); pay columns in neither are the employer's (e.g. ER Pension). Without those
/// formulas, columns before Tax are earnings and the rest deductions. Formula cells are read as Excel last saved them.
/// </remarks>
public static partial class PayslipWorkbook
{
    private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("en-GB");

    private static readonly string[] YearHeaders = ["Year"];
    private static readonly string[] MonthHeaders = ["Period", "Month", "Tax month"];
    private static readonly string[] DateHeaders = ["Date", "Pay date", "Paid"];
    private static readonly string[] PensionReceivedHeaders = ["Transfer Date", "Pension received"];
    private static readonly string[] NotesHeaders = ["Notes", "Note"];

    public static WorkbookContents Read(Stream xlsx)
    {
        using var workbook = new XLWorkbook(xlsx);
        return Read(workbook.Worksheet(1));
    }

    public static WorkbookContents Read(IXLWorksheet sheet)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var header = Enumerable.Range(1, Math.Min(10, lastRow)).Select(sheet.Row)
            .FirstOrDefault(r => FindColumn(r, YearHeaders) is not null && FindColumn(r, DateHeaders) is not null);
        if (header is null)
            return Failed("No heading row with Year and Date in the first 10 rows of the first sheet.");

        var year = FindColumn(header, YearHeaders)!.Value;
        var date = FindColumn(header, DateHeaders)!.Value;
        if (FindColumn(header, MonthHeaders) is not { } month)
            return Failed("No Period (tax month) column.");
        if (FindColumn(header, ["Gross"]) is not { } gross)
            return Failed("No Gross column: the pay columns are the ones between Date and Gross.");
        var deductionsTotal = FindColumn(header, ["Deductions"]);
        var net = FindColumn(header, ["Net"]);
        var pensionReceived = FindColumn(header, PensionReceivedHeaders);
        var notes = FindColumn(header, NotesHeaders);

        // The pay columns: between Date and Gross, with a heading, other than the optional ones.
        var pay = Enumerable.Range(date + 1, gross - date - 1)
            .Where(c => c != pensionReceived && c != notes && Text(header.Cell(c)) is not null)
            .ToList();
        if (pay.Count == 0) return Failed("No pay columns between Date and Gross.");

        var firstRow = Enumerable.Range(header.RowNumber() + 1, Math.Max(0, lastRow - header.RowNumber()))
            .FirstOrDefault(r => Date(sheet.Cell(r, date)) is not null);
        var earningsRange = firstRow > 0 ? SumRange(sheet.Cell(firstRow, gross)) : null;
        var deductionsRange = firstRow > 0 && deductionsTotal is { } dt ? SumRange(sheet.Cell(firstRow, dt)) : null;
        var fromFormulas = earningsRange is not null && deductionsRange is not null;
        var taxColumn = pay.FirstOrDefault(c => string.Equals(Text(header.Cell(c)), "Tax", StringComparison.OrdinalIgnoreCase));

        PayElementKind KindOf(int column) => fromFormulas
            ? column >= earningsRange!.Value.From && column <= earningsRange.Value.To ? PayElementKind.Earning
            : column >= deductionsRange!.Value.From && column <= deductionsRange.Value.To ? PayElementKind.Deduction
            : PayElementKind.EmployerContribution
            : taxColumn > 0 && column >= taxColumn ? PayElementKind.Deduction : PayElementKind.Earning;

        var columns = new List<WorkbookColumn>();
        foreach (var c in pay)
        {
            var heading = Text(header.Cell(c))!;
            var kind = KindOf(c);
            columns.Add(new WorkbookColumn(columns.Count, header.Cell(c).Address.ColumnLetter, heading, heading, kind, GuessCategory(heading, kind)));
        }
        columns = UniqueNames(columns);

        var rows = new List<WorkbookRow>();
        var skipped = new List<(int, string)>();
        for (var r = header.RowNumber() + 1; r <= lastRow; r++)
        {
            if (Date(sheet.Cell(r, date)) is not { } payDate) continue;   // not paid yet, or not a payslip row
            if (Number(sheet.Cell(r, year)) is not { } yearEnd || Number(sheet.Cell(r, month)) is not { } period ||
                period != Math.Floor(period) || period is < 1 or > 12)
            {
                skipped.Add((r, "no Year, or a Period that isn't 1 to 12"));
                continue;
            }
            var (taxYear, taxMonth) = ((int)yearEnd - 1, (int)period);
            if (payDate < TaxYears.Start(taxYear) || payDate > TaxYears.End(taxYear))
            {
                skipped.Add((r, $"the date {payDate.ToString("dd/MM/yyyy", UkCulture)} isn't in {TaxYears.Label(taxYear)}"));
                continue;
            }

            var amounts = new Dictionary<int, decimal>();
            foreach (var column in columns)
                if (Number(sheet.Cell(r, pay[column.Index])) is { } amount && Math.Round(amount, 2) != 0)
                    amounts[column.Index] = Math.Round(amount, 2);
            if (amounts.Count == 0)
            {
                skipped.Add((r, "no amounts"));
                continue;
            }

            rows.Add(new WorkbookRow(r, taxYear, taxMonth, payDate,
                pensionReceived is { } pr ? Date(sheet.Cell(r, pr)) : null,
                notes is { } n && sheet.Cell(r, n) is { HasFormula: false } noteCell && noteCell.CachedValue.IsText ? Text(noteCell) : null,
                amounts,
                Number(sheet.Cell(r, gross)) is { } g ? Math.Round(g, 2) : null,
                net is { } nc && Number(sheet.Cell(r, nc)) is { } nt ? Math.Round(nt, 2) : null));
        }

        return new WorkbookContents(columns, rows, [], fromFormulas, skipped);
    }

    /// <summary>A guess at a pay element's category from its heading, for the preview to confirm.</summary>
    public static PayCategory GuessCategory(string heading, PayElementKind kind)
    {
        var h = heading.ToLowerInvariant();
        bool Has(params string[] words) => words.Any(h.Contains);
        if (Has("pension")) return PayCategory.Pension;
        if (kind == PayElementKind.Deduction)
        {
            if (h is "tax" or "paye" || Has("income tax")) return PayCategory.Tax;
            if (h is "ni" or "nic" || Has("national insurance")) return PayCategory.NationalInsurance;
            return PayCategory.Other;
        }
        if (Has("salary", "basic")) return PayCategory.Salary;
        if (Has("bonus", "xmas", "christmas", "commission")) return PayCategory.Bonus;
        if (h is "o/t" || Has("overtime")) return PayCategory.Overtime;
        if (Has("back pay", "backpay", "arrears")) return PayCategory.BackPay;
        if (h is "adj" || Has("adjust")) return PayCategory.Other;
        return kind == PayElementKind.Earning ? PayCategory.Allowance : PayCategory.Other;
    }

    /// <summary>A heading used again (e.g. an earnings Adj and a deductions Adj) gets its kind added, or else its column.</summary>
    private static List<WorkbookColumn> UniqueNames(List<WorkbookColumn> columns)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return columns.Select(c =>
        {
            var name = c.Header;
            if (!used.Add(name) && !used.Add(name = $"{c.Header} ({KindLabel(c.Kind)})")) used.Add(name = $"{c.Header} ({c.Letter})");
            return c with { Name = name };
        }).ToList();
    }

    private static string KindLabel(PayElementKind kind) => kind switch
    {
        PayElementKind.Earning => "earning",
        PayElementKind.Deduction => "deduction",
        _ => "employer"
    };

    private static WorkbookContents Failed(string problem) => new([], [], [problem], false, []);

    private static int? FindColumn(IXLRow row, string[] names) =>
        row.CellsUsed().FirstOrDefault(c => Text(c) is { } t && names.Contains(t, StringComparer.OrdinalIgnoreCase))?.Address.ColumnNumber;

    /// <summary>The columns of a <c>=SUM(D2:N2)</c> formula, or null if the cell isn't one.</summary>
    private static (int From, int To)? SumRange(IXLCell cell)
    {
        if (!cell.HasFormula || SumFormula().Match(cell.FormulaA1.Replace("$", "")) is not { Success: true } m) return null;
        return (XLHelper.GetColumnNumberFromLetter(m.Groups[1].Value), XLHelper.GetColumnNumberFromLetter(m.Groups[2].Value));
    }

    [GeneratedRegex(@"^=?SUM\(([A-Z]+)\d+:([A-Z]+)\d+\)$", RegexOptions.IgnoreCase)]
    private static partial Regex SumFormula();

    // --- Cells: as Excel last saved them (CachedValue); a formula with no saved value (a workbook Excel hasn't opened)
    //     is worked out. ---

    private static XLCellValue Value(IXLCell cell) =>
        cell.HasFormula && cell.CachedValue.IsBlank ? cell.Value : cell.CachedValue;

    private static string? Text(IXLCell cell)
    {
        var v = Value(cell);
        var s = v.Type switch
        {
            XLDataType.Blank or XLDataType.Error => null,
            XLDataType.Text => v.GetText(),
            _ => cell.GetFormattedString()
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static decimal? Number(IXLCell cell)
    {
        var v = Value(cell);
        if (v.IsNumber) return (decimal)v.GetNumber();
        if (v.IsText && decimal.TryParse(v.GetText().Trim().TrimStart('£'), NumberStyles.Float, UkCulture, out var d)) return d;
        return null;
    }

    private static DateOnly? Date(IXLCell cell)
    {
        var v = Value(cell);
        if (v.IsDateTime) return DateOnly.FromDateTime(v.GetDateTime());
        if (v.IsText && DateTime.TryParse(v.GetText(), UkCulture, DateTimeStyles.None, out var dt)) return DateOnly.FromDateTime(dt);
        return null;
    }
}
