using ClosedXML.Excel;
using GreenHedges.HomeServer.Utilities.Models;

namespace GreenHedges.HomeServer.Utilities.Services.Import;

/// <summary>A reading from the sheet.</summary>
/// <param name="Row">Its row in the sheet, for messages.</param>
/// <param name="SheetUnitsPerDay">The sheet's own Units/Day for the row, to check the figures worked out here against.</param>
public sealed record SheetReading(int Row, DateOnly Date, decimal Value, decimal? SheetUnitsPerDay);

/// <summary>A utility's block of the sheet: its readings as they come, and the rows that couldn't be read, with why.</summary>
public sealed record SheetUtility(UtilityType Utility, List<SheetReading> Readings, List<(int Row, string Reason)> Skipped);

/// <param name="Problems">Why nothing can be imported; empty when the sheet could be read.</param>
public sealed record WorkbookContents(List<SheetUtility> Utilities, List<string> Problems);

/// <summary>
/// Reads the meter readings spreadsheet: one sheet with a block of columns per utility, each a title (Electricity, Gas,
/// Water…) above the headings Date and Reading, then a row per reading. Only those two columns are read, and the
/// sheet's Units/Day beside them to check against; a utility's second block (an old waste water one) and anything
/// else on the sheet are ignored. Pure.
/// </summary>
public static class ReadingsWorkbook
{
    /// <summary>The heading row is looked for this far down.</summary>
    private const int HeadingRows = 10;

    public static WorkbookContents Read(Stream xlsx)
    {
        using var workbook = new XLWorkbook(xlsx);
        var sheet = workbook.Worksheets.First();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        var utilities = new List<SheetUtility>();
        for (var row = 2; row <= Math.Min(HeadingRows, lastRow) && utilities.Count == 0; row++)
            for (var column = 1; column < lastColumn; column++)
            {
                if (!IsHeading(sheet.Cell(row, column), "Date") || !IsHeading(sheet.Cell(row, column + 1), "Reading")) continue;
                if (UtilityOf(Text(sheet.Cell(row - 1, column))) is not { } utility || utilities.Any(u => u.Utility == utility)) continue;
                utilities.Add(ReadBlock(sheet, utility, row, column, lastRow));
            }

        List<string> problems = utilities.Count > 0 ? [] :
        [
            "No readings found: the first sheet needs a title (Electricity, Gas or Water) above the headings Date and Reading for each utility."
        ];
        return new WorkbookContents(utilities.OrderBy(u => u.Utility).ToList(), problems);
    }

    private static SheetUtility ReadBlock(IXLWorksheet sheet, UtilityType utility, int headingRow, int column, int lastRow)
    {
        // The sheet's own Units/Day, where it has one in the block's fifth column.
        int? perDayColumn = IsHeading(sheet.Cell(headingRow, column + 4), "Units/Day") ? column + 4 : null;
        var readings = new List<SheetReading>();
        var skipped = new List<(int, string)>();

        for (var row = headingRow + 1; row <= lastRow; row++)
        {
            var (dateValue, readingValue) = (Value(sheet.Cell(row, column)), Value(sheet.Cell(row, column + 1)));
            if (dateValue.IsBlank && readingValue.IsBlank) continue;

            if (!dateValue.IsDateTime) skipped.Add((row, dateValue.IsBlank ? "it has a reading but no date" : "its date isn't a date"));
            else if (!readingValue.IsNumber) skipped.Add((row, readingValue.IsBlank ? "it has a date but no reading" : "its reading isn't a number"));
            else if (readingValue.GetNumber() is < 0 or > 999_999_999) skipped.Add((row, "its reading is out of range"));
            else
            {
                var perDay = perDayColumn is { } c && Value(sheet.Cell(row, c)) is { IsNumber: true } v ? (decimal?)(decimal)v.GetNumber() : null;
                // Three places, as readings are kept: also drops a double's noise (35625.8 as 35625.800000000003).
                readings.Add(new SheetReading(row, DateOnly.FromDateTime(dateValue.GetDateTime()),
                    Math.Round((decimal)readingValue.GetNumber(), 3), perDay));
            }
        }

        return new SheetUtility(utility, readings, skipped);
    }

    /// <summary>The utility a block's title names: "Electricity", "Gas", "Water In"…</summary>
    private static UtilityType? UtilityOf(string? title) =>
        title is null ? null
        : Enum.GetValues<UtilityType>().Cast<UtilityType?>()
            .FirstOrDefault(u => title.StartsWith(u!.Value.ToString(), StringComparison.OrdinalIgnoreCase));

    private static bool IsHeading(IXLCell cell, string heading) =>
        string.Equals(Text(cell), heading, StringComparison.OrdinalIgnoreCase);

    private static string? Text(IXLCell cell) => Value(cell) is { IsText: true } v ? v.GetText().Trim() : null;

    /// <summary>
    /// A cell as Excel last saved it (the cached value); a formula with no saved value (a workbook Excel hasn't
    /// opened, as the tests') is worked out.
    /// </summary>
    private static XLCellValue Value(IXLCell cell) =>
        cell.HasFormula && cell.CachedValue.IsBlank ? cell.Value : cell.CachedValue;
}
