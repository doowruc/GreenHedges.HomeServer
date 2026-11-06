using System.Globalization;

namespace GreenHedges.HomeServer.PayAndPensions.Services.Import;

/// <summary>A pasted row that can be imported. <paramref name="Row"/> is its line in the paste (1 is the heading row).</summary>
public sealed record PastedStatement(int Row, DateOnly Date, string? Label, decimal Value, decimal? ProjectedFund, decimal? ProjectedAnnualIncome);

/// <summary>What a paste holds: the rows to import, those skipped (and why), and the columns it ignored.</summary>
/// <param name="Error">Why nothing could be read, e.g. no Date or Value heading; null when it could.</param>
/// <param name="StartDate">The date of a first row with a value of 0, as a spreadsheet's starting row: the pension's start.</param>
public sealed record StatementPasteResult(string? Error, List<PastedStatement> Rows, List<string> Skipped, List<string> Ignored, DateOnly? StartDate);

/// <summary>
/// Reads statements pasted from a spreadsheet: tab-separated rows, as Excel copies them, with a heading row first
/// (anything above it, e.g. the sheet's title, is ignored).
/// Columns are found by heading (ignoring case): Date; Value; Label or When; and for a type with projections,
/// Retirement (or Fund, or At retirement) and Annual (or Annual income, or Income). Others, e.g. a sheet's worked-out
/// Change, are ignored. Dates as Excel shows them in the UK (or as its serial numbers); amounts with or without £ and
/// commas. Pure, so unit tested; text is formatted in en-GB, as outside a request.
/// </summary>
public static class StatementPaste
{
    /// <summary>As <c>PensionStatement.Label</c>.</summary>
    public const int MaxLabel = 50;

    private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("en-GB");

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "dd-MMM-yy", "d-MMM-yy", "dd-MMM-yyyy", "d-MMM-yyyy",
        "d MMM yyyy", "d MMMM yyyy", "d MMM yy", "yyyy-MM-dd"
    ];

    private static readonly Dictionary<string, string> Headings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Date"] = "Date", ["Value"] = "Value", ["Label"] = "Label", ["When"] = "Label",
        ["Retirement"] = "Fund", ["Fund"] = "Fund", ["At retirement"] = "Fund", ["Projected fund"] = "Fund",
        ["Annual"] = "Income", ["Annual income"] = "Income", ["Income"] = "Income", ["Projected annual income"] = "Income",
    };

    public static StatementPasteResult Parse(string? text, bool hasProjections)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select((line, i) => (Row: i + 1, Cells: line.Split('\t').Select(c => c.Trim()).ToArray()))
            .Where(l => l.Cells.Any(c => c.Length > 0))
            .ToList();
        if (lines.Count == 0) return Failed("Paste the rows, with their heading row first.");

        // The heading row is the first with Date and Value, so a title row above it (as a sheet often has) can come too.
        var headingAt = lines.FindIndex(l => l.Cells.Contains("Date", StringComparer.OrdinalIgnoreCase) && l.Cells.Contains("Value", StringComparer.OrdinalIgnoreCase));
        if (headingAt < 0)
            return Failed($"The first row must be the headings, with Date and Value (it's \"{string.Join(", ", lines[0].Cells.Where(c => c.Length > 0))}\").");

        // Each heading's first column; Excel puts nothing before the first copied cell.
        var columns = new Dictionary<string, int>();
        var ignored = new List<string>();
        var heading = lines[headingAt];
        for (var i = 0; i < heading.Cells.Length; i++)
        {
            var name = heading.Cells[i];
            if (name.Length == 0) continue;
            if (Headings.TryGetValue(name, out var field) && (hasProjections || field is not ("Fund" or "Income")) && columns.TryAdd(field, i)) continue;
            ignored.Add(name);
        }
        var rows = new List<PastedStatement>();
        var skipped = new List<string>();
        var seen = new Dictionary<DateOnly, int>();
        DateOnly? start = null;
        foreach (var (row, cells) in lines.Skip(headingAt + 1))
        {
            string Cell(string field) => columns.TryGetValue(field, out var i) && i < cells.Length ? cells[i] : string.Empty;
            var (dateText, valueText) = (Cell("Date"), Cell("Value"));
            if (dateText.Length == 0 && valueText.Length == 0) continue;   // e.g. a shorter column beside a longer one
            if (!TryDate(dateText, out var date)) { skipped.Add($"Row {row}: \"{dateText}\" isn't a date."); continue; }
            if (valueText.Length == 0) { skipped.Add($"Row {row} ({Show(date)}): no value."); continue; }
            if (!TryAmount(valueText, out var value)) { skipped.Add($"Row {row} ({Show(date)}): \"{valueText}\" isn't an amount."); continue; }
            if (value == 0)
            {
                if (rows.Count == 0 && skipped.Count == 0 && start is null) start = date;
                skipped.Add($"Row {row} ({Show(date)}): a value of 0{(start == date ? ", taken as the start" : "")}.");
                continue;
            }
            var (fundText, incomeText) = (Cell("Fund"), Cell("Income"));
            if (!TryOptionalAmount(fundText, out var fund)) { skipped.Add($"Row {row} ({Show(date)}): \"{fundText}\" isn't an amount."); continue; }
            if (!TryOptionalAmount(incomeText, out var income)) { skipped.Add($"Row {row} ({Show(date)}): \"{incomeText}\" isn't an amount."); continue; }
            if (seen.TryGetValue(date, out var first)) { skipped.Add($"Row {row} ({Show(date)}): the same date as row {first}."); continue; }

            seen[date] = row;
            var label = Cell("Label");
            if (label.Length > MaxLabel) { skipped.Add($"Row {row} ({Show(date)}): the label is over {MaxLabel} characters."); continue; }
            rows.Add(new PastedStatement(row, date, label.Length > 0 ? label : null, value, fund, income));
        }
        return new StatementPasteResult(null, rows.OrderBy(r => r.Date).ToList(), skipped, ignored, start);
    }

    private static StatementPasteResult Failed(string error) => new(error, [], [], [], null);

    private static string Show(DateOnly date) => date.ToString("dd/MM/yyyy", UkCulture);

    private static bool TryDate(string text, out DateOnly date)
    {
        if (DateOnly.TryParseExact(text, DateFormats, UkCulture, DateTimeStyles.None, out date)) return true;
        // A date cell formatted as a number pastes as Excel's serial number.
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var serial) && serial is > 10_000 and < 100_000)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }
        return false;
    }

    private static bool TryAmount(string text, out decimal amount)
    {
        var plain = text.Replace("£", "").Replace(",", "").Replace(" ", "");
        var negative = plain.StartsWith('(') && plain.EndsWith(')');
        if (negative) plain = plain[1..^1];
        var ok = decimal.TryParse(plain, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
        if (negative) amount = -amount;
        amount = Math.Round(amount, 2);
        return ok;
    }

    /// <summary>A blank cell is no amount; anything else must be one.</summary>
    private static bool TryOptionalAmount(string text, out decimal? amount)
    {
        amount = null;
        if (text.Length == 0) return true;
        if (!TryAmount(text, out var value)) return false;
        amount = value;
        return true;
    }
}
