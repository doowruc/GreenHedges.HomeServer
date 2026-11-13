using ClosedXML.Excel;

namespace GreenHedges.HomeServer.Presents.Services.Import;

/// <summary>A present from a year's sheet.</summary>
/// <param name="Row">Its row in the sheet, for messages.</param>
/// <param name="Who">The child's name, as the sheet has it (tidied).</param>
/// <param name="Amount">What it counted as there: the Amount as Excel saved it, rounded to the penny.</param>
public sealed record SheetPurchase(int Row, DateOnly? Date, string? Shop, string Item, string Who, decimal Amount);

/// <summary>A cash row (Item "Cash") from a year's sheet: not imported, as the app works cash out, but checked against.</summary>
public sealed record SheetCash(int Row, string Who, decimal Amount);

/// <summary>A year's sheet: its presents, its cash rows, and the rows that couldn't be read, with why.</summary>
public sealed record SheetYear(int Year, List<SheetPurchase> Purchases, List<SheetCash> Cash, List<(int Row, string Reason)> Skipped);

/// <param name="Problems">Why nothing can be imported; empty when the workbook could be read.</param>
/// <param name="Warnings">Sheets that were left out, and why, though others could be read.</param>
public sealed record WorkbookContents(List<SheetYear> Years, List<string> Problems, List<string> Warnings);

/// <summary>
/// Reads the presents workbook: a sheet a year, named by the year, each with the headings Date, Shop, Item, Who and
/// Amount side by side in its first rows and a row per present below. Only those five columns are read: the pivot
/// beside them, any other list (one year has one for other people's presents) and other sheets are ignored. Pure.
/// </summary>
public static class PresentsWorkbook
{
    /// <summary>The heading row is looked for this far down.</summary>
    private const int HeadingRows = 10;

    private static readonly string[] Headings = ["Date", "Shop", "Item", "Who", "Amount"];

    public static WorkbookContents Read(Stream xlsx)
    {
        using var workbook = new XLWorkbook(xlsx);
        var years = new List<SheetYear>();
        var warnings = new List<string>();

        foreach (var sheet in workbook.Worksheets)
        {
            if (!int.TryParse(sheet.Name.Trim(), out var year) || year is < 2000 or > 2999) continue;
            if (years.Any(y => y.Year == year)) continue;
            if (FindHeadings(sheet) is not { } at)
            {
                warnings.Add($"The {year} sheet has no headings Date, Shop, Item, Who and Amount side by side in its first {HeadingRows} rows, so it's left out.");
                continue;
            }
            years.Add(ReadYear(sheet, year, at.Row, at.Column));
        }

        List<string> problems = years.Count > 0 ? [] :
            ["No years found: the workbook needs a sheet per year, named by the year (e.g. 2025), with the headings Date, Shop, Item, Who and Amount.", .. warnings];
        return new WorkbookContents(years.OrderBy(y => y.Year).ToList(), problems, years.Count > 0 ? warnings : []);
    }

    private static (int Row, int Column)? FindHeadings(IXLWorksheet sheet)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var row = 1; row <= HeadingRows; row++)
            for (var column = 1; column + Headings.Length - 1 <= lastColumn; column++)
                if (Headings.Select((h, i) => IsHeading(sheet.Cell(row, column + i), h)).All(ok => ok))
                    return (row, column);
        return null;
    }

    private static SheetYear ReadYear(IXLWorksheet sheet, int year, int headingRow, int column)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var purchases = new List<SheetPurchase>();
        var cash = new List<SheetCash>();
        var skipped = new List<(int, string)>();

        for (var row = headingRow + 1; row <= lastRow; row++)
        {
            var cells = Enumerable.Range(0, Headings.Length).Select(i => Value(sheet.Cell(row, column + i))).ToArray();
            var (date, shop, item, who, amount) = (cells[0], cells[1], cells[2], cells[3], cells[4]);
            if (cells.All(c => c.IsBlank)) continue;

            var itemText = Tidy(item);
            var whoText = Tidy(who);
            if (itemText is null) skipped.Add((row, "it has no item"));
            else if (whoText is null) skipped.Add((row, "it doesn't say who it's for"));
            else if (!amount.IsNumber) skipped.Add((row, amount.IsBlank ? "it has no amount" : "its amount isn't a number"));
            else if (Math.Round((decimal)amount.GetNumber(), 2) is var value && value is <= 0 or > 99_999_999.99m)
                skipped.Add((row, "its amount isn't more than £0"));
            else if (itemText.Length > 200 || whoText.Length > 50 || Tidy(shop)?.Length > 100)
                skipped.Add((row, "its item, shop or name is too long"));
            else if (string.Equals(itemText, "Cash", StringComparison.OrdinalIgnoreCase))
                cash.Add(new SheetCash(row, whoText, value));
            else
                purchases.Add(new SheetPurchase(row, date.IsDateTime ? DateOnly.FromDateTime(date.GetDateTime()) : null, Tidy(shop), itemText, whoText, value));
        }

        return new SheetYear(year, purchases, cash, skipped);
    }

    /// <summary>A cell's text (or number, as text), with spaces tidied; null if it's blank.</summary>
    private static string? Tidy(XLCellValue value)
    {
        var text = value.IsText ? value.GetText() : value.IsNumber ? value.GetNumber().ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return string.Join(' ', (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)) is { Length: > 0 } tidy ? tidy : null;
    }

    private static bool IsHeading(IXLCell cell, string heading) =>
        Value(cell) is { IsText: true } v && string.Equals(v.GetText().Trim(), heading, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A cell as Excel last saved it (the cached value); a formula with no saved value (a workbook Excel hasn't
    /// opened, as the tests') is worked out.
    /// </summary>
    private static XLCellValue Value(IXLCell cell) =>
        cell.HasFormula && cell.CachedValue.IsBlank ? cell.Value : cell.CachedValue;
}
