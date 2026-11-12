using ClosedXML.Excel;

namespace GreenHedges.HomeServer.Utilities.Tests.Infrastructure;

/// <summary>
/// Builds made-up meter readings spreadsheets laid out like the real one: a block of five columns per utility with a
/// gap between (a title above Date, Reading, Units, Days, Units/Day; the last three formulas), one row per reading.
/// </summary>
public sealed class TestWorkbook
{
    private readonly List<(string Title, List<(object? Date, object? Reading)> Rows)> _blocks = [];

    /// <summary>Text below one of the blocks, as the real sheet has: (block, row offset from the first reading, column offset, text).</summary>
    public List<(int Block, int Row, int Column, string Text)> Strays { get; } = [];

    /// <summary>Headings to use instead of Date and Reading.</summary>
    public (string Date, string Reading) Headings { get; init; } = ("Date", "Reading");

    /// <summary>A utility's block, titled e.g. "Electricity" or "Water In". A row's date or reading can be null or text.</summary>
    public TestWorkbook Block(string title, params (object? Date, object? Reading)[] rows)
    {
        _blocks.Add((title, rows.ToList()));
        return this;
    }

    public static (object? Date, object? Reading) R(int year, int month, int day, double reading) => (new DateOnly(year, month, day), reading);

    public byte[] Build()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        static string L(int column) => XLHelper.GetColumnLetterFromNumber(column);

        for (var b = 0; b < _blocks.Count; b++)
        {
            var (title, rows) = _blocks[b];
            var c = b * 6 + 1;
            sheet.Cell(1, c).Value = title;
            string[] headings = [Headings.Date, Headings.Reading, "Units", "Days", "Units/Day"];
            for (var i = 0; i < headings.Length; i++) sheet.Cell(2, c + i).Value = headings[i];

            for (var r = 0; r < rows.Count; r++)
            {
                var row = r + 3;
                Set(sheet.Cell(row, c), rows[r].Date);
                Set(sheet.Cell(row, c + 1), rows[r].Reading);
                if (r == 0 || rows[r].Date is not DateOnly || rows[r].Reading is not double) continue;
                // As the real sheet: a reading lower than the one before is a new meter, counted from 0.
                var fell = rows[r - 1].Reading is double before && (double)rows[r].Reading! < before;
                sheet.Cell(row, c + 2).FormulaA1 = fell ? $"{L(c + 1)}{row}-0" : $"{L(c + 1)}{row}-{L(c + 1)}{row - 1}";
                sheet.Cell(row, c + 3).FormulaA1 = $"{L(c)}{row}-{L(c)}{row - 1}";
                sheet.Cell(row, c + 4).FormulaA1 = $"{L(c + 2)}{row}/{L(c + 3)}{row}";
            }
        }
        foreach (var (block, row, column, text) in Strays) sheet.Cell(row + 3, block * 6 + 1 + column).Value = text;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Set(IXLCell cell, object? value)
    {
        switch (value)
        {
            case DateOnly date: cell.Value = date.ToDateTime(TimeOnly.MinValue); break;
            case double number: cell.Value = number; break;
            case string text: cell.Value = text; break;
        }
    }
}
