using ClosedXML.Excel;

namespace GreenHedges.HomeServer.Presents.Tests.Infrastructure;

/// <summary>
/// Builds made-up presents workbooks laid out like the real one: a sheet a year with the headings Date, Shop, Item, Who
/// and Amount in A-E and a row per present, plus a pivot-like summary beside it and, optionally, another list further
/// right, which the import must ignore. Made-up names and amounts only.
/// </summary>
public sealed class TestWorkbook
{
    private readonly List<(string Name, int HeadingRow, List<Row> Rows, bool Extras)> _sheets = [];

    /// <summary>A row: a date (DateOnly, text or null), shop, item, who, and an amount (a number, or a formula starting "=").</summary>
    public sealed record Row(object? Date, string? Shop, string? Item, string? Who, object? Amount);

    public static Row R(int year, int month, int day, string? shop, string? item, string? who, object? amount) =>
        new(new DateOnly(year, month, day), shop, item, who, amount);

    /// <summary>A year's sheet named <paramref name="name"/> (usually the year), headings in <paramref name="headingRow"/>.</summary>
    public TestWorkbook Sheet(string name, params Row[] rows) => Sheet(name, 1, true, rows);

    public TestWorkbook Sheet(string name, int headingRow, bool extras, params Row[] rows)
    {
        _sheets.Add((name, headingRow, rows.ToList(), extras));
        return this;
    }

    public byte[] Build()
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, headingRow, rows, extras) in _sheets)
        {
            var sheet = workbook.AddWorksheet(name);
            string[] headings = ["Date", "Shop", "Item", "Who", "Amount"];
            for (var i = 0; i < headings.Length; i++) sheet.Cell(headingRow, i + 1).Value = headings[i];
            for (var r = 0; r < rows.Count; r++)
            {
                var row = headingRow + 1 + r;
                var (date, shop, item, who, amount) = (rows[r].Date, rows[r].Shop, rows[r].Item, rows[r].Who, rows[r].Amount);
                Set(sheet.Cell(row, 1), date);
                Set(sheet.Cell(row, 2), shop);
                Set(sheet.Cell(row, 3), item);
                Set(sheet.Cell(row, 4), who);
                Set(sheet.Cell(row, 5), amount);
            }
            if (extras)
            {
                // As the real sheets: a pivot of totals in G-H, and a difference formula; another list from M.
                sheet.Cell(headingRow + 2, 7).Value = "Row Labels";
                sheet.Cell(headingRow + 2, 8).Value = "Sum of Amount";
                sheet.Cell(headingRow + 3, 7).Value = "Someone";
                sheet.Cell(headingRow + 3, 8).Value = 123.45;
                sheet.Cell(headingRow + 3, 10).FormulaA1 = "H4-1";
                string[] other = ["Shop", "Item", "For", "From", "Amount", "Method"];
                for (var i = 0; i < other.Length; i++) sheet.Cell(headingRow, 13 + i).Value = other[i];
                sheet.Cell(headingRow + 1, 13).Value = "Some Shop";
                sheet.Cell(headingRow + 1, 14).Value = "Soap";
                sheet.Cell(headingRow + 1, 17).Value = 5;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Set(IXLCell cell, object? value)
    {
        switch (value)
        {
            case DateOnly date: cell.Value = date.ToDateTime(TimeOnly.MinValue); break;
            case string formula when formula.StartsWith('='): cell.FormulaA1 = formula[1..]; break;
            case string text: cell.Value = text; break;
            case double number: cell.Value = number; break;
            case int number: cell.Value = number; break;
            case decimal number: cell.Value = (double)number; break;
        }
    }
}
