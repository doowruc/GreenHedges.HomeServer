using ClosedXML.Excel;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

/// <summary>
/// Builds made-up payslip spreadsheets laid out like the real ones: Year (the year the tax year ends), Period, Date,
/// the pay columns, then Gross, Deductions and Net as formulas over them, and more worked-out columns after.
/// </summary>
public sealed class TestWorkbook
{
    private readonly string[] _earnings;
    private readonly string[] _deductions;
    private readonly string[] _employer;
    private readonly List<object?[]> _rows = [];

    /// <param name="employer">Pay columns after the deductions, in neither formula (e.g. ER Pension).</param>
    public TestWorkbook(string[] earnings, string[] deductions, string[]? employer = null)
    {
        (_earnings, _deductions, _employer) = (earnings, deductions, employer ?? []);
    }

    /// <summary>Extra headed columns between the pay columns and Gross (e.g. Transfer Date), and after Net (e.g. Notes).</summary>
    public string[] BeforeGross { get; init; } = [];
    public string[] AfterNet { get; init; } = [];

    /// <summary>Gross and Deductions as numbers rather than formulas, as a spreadsheet without them would be.</summary>
    public bool NoFormulas { get; init; }

    /// <summary>
    /// A row: year (the tax year's end), period, date (null for a month not paid yet), then a value for each pay column
    /// in order, then each <see cref="BeforeGross"/> and <see cref="AfterNet"/> column. A string starting "=" is a formula.
    /// </summary>
    public TestWorkbook Row(int year, int period, DateOnly? date, params object?[] values)
    {
        _rows.Add([year, period, date, .. values]);
        return this;
    }

    public MemoryStream Build()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        string[] pay = [.. _earnings, .. _deductions, .. _employer];
        string[] headings = ["Year", "Period", "Date", .. pay, .. BeforeGross, "Gross", "Deductions", "Net", "Annual", .. AfterNet];
        for (var c = 0; c < headings.Length; c++) sheet.Cell(1, c + 1).Value = headings[c];

        static string Letter(int column) => XLHelper.GetColumnLetterFromNumber(column);
        var firstPay = 4;
        var firstDeduction = firstPay + _earnings.Length;
        var gross = 4 + pay.Length + BeforeGross.Length;
        for (var r = 0; r < _rows.Count; r++)
        {
            var row = r + 2;
            var values = _rows[r];
            sheet.Cell(row, 1).Value = (int)values[0]!;
            sheet.Cell(row, 2).Value = (int)values[1]!;
            if (values[2] is DateOnly date) sheet.Cell(row, 3).Value = date.ToDateTime(TimeOnly.MinValue);
            for (var i = 3; i < values.Length; i++)
            {
                var column = i + 1 + (i - 3 >= pay.Length + BeforeGross.Length ? 4 : 0);   // after Net for AfterNet values
                var cell = sheet.Cell(row, column);
                switch (values[i])
                {
                    case string s when s.StartsWith('='): cell.FormulaA1 = s; break;
                    case string s: cell.Value = s; break;
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); break;
                    case null: break;
                    default: cell.Value = Convert.ToDouble(values[i]); break;
                }
            }
            if (values[2] is null) continue;   // the sheets leave a future month's totals empty
            var earnings = $"{Letter(firstPay)}{row}:{Letter(firstDeduction - 1)}{row}";
            var deductions = $"{Letter(firstDeduction)}{row}:{Letter(firstDeduction + _deductions.Length - 1)}{row}";
            if (NoFormulas)
            {
                sheet.Cell(row, gross).Value = sheet.Range(earnings).CellsUsed().Sum(c => c.GetDouble());
                sheet.Cell(row, gross + 1).Value = sheet.Range(deductions).CellsUsed().Sum(c => c.GetDouble());
            }
            else
            {
                sheet.Cell(row, gross).FormulaA1 = $"=SUM({earnings})";
                sheet.Cell(row, gross + 1).FormulaA1 = $"=SUM({deductions})";
            }
            sheet.Cell(row, gross + 2).FormulaA1 = $"={Letter(gross)}{row}-{Letter(gross + 1)}{row}";
            sheet.Cell(row, gross + 3).FormulaA1 = $"={Letter(firstPay)}{row}*12";
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    public byte[] Bytes() => Build().ToArray();
}
