using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.PayAndPensions.Models;

namespace GreenHedges.HomeServer.PayAndPensions.Services.Import;

/// <summary>A readable line of a transaction export. <paramref name="Row"/> is its line in the file (1 is the first).</summary>
public sealed record CsvTransaction(int Row, DateOnly TradeDate, DateOnly? SettleDate, string Reference, string Description,
    TransactionKind Kind, string? Fund, decimal? UnitPrice, decimal? Quantity, decimal Value)
{
    /// <summary>What makes two lines the same transaction, in the file and in the database.</summary>
    public TransactionKey Key => new(TradeDate, Reference, Description, Value);
}

/// <summary>A transaction's identity for spotting ones already imported: an export has no id of its own.</summary>
public readonly record struct TransactionKey(DateOnly TradeDate, string Reference, string Description, decimal Value);

/// <summary>What a file holds: its transactions, and the lines skipped (and why).</summary>
/// <param name="Error">Why nothing could be read, e.g. no heading row; null when it could.</param>
/// <param name="MovementDeals">A security movements export's Bought and Sold lines, which aren't read; null for any other file.</param>
public sealed record TransactionCsvResult(string? Error, List<CsvTransaction> Rows, List<string> Skipped, int? MovementDeals = null);

/// <summary>
/// Reads a pension provider's transaction history exported as CSV: a few lines about the account (ignored, and never
/// kept: they hold the client's name and number), then a heading row with Trade date and Value, then a line per
/// transaction. Columns by heading: Trade date, Settle date, Reference, Description, Unit cost (p), Quantity, Value (£).
/// Dates are UK; amounts may have commas, and "n/a" means none. Each line's kind and fund are worked out from its
/// reference and description (<see cref="Kind"/>, <see cref="Fund"/>).
/// <para>
/// Also reads a fund's security movements export (a first line naming the fund, then the account's lines, then a heading
/// row with Date, Type and Total cost), for what the history leaves out: its corporate actions' cost adjustments and
/// its conversions. Bought and Sold lines are ignored, as the history has them with exact units. Pure, so unit tested;
/// text in en-GB.
/// </para>
/// </summary>
public static class TransactionCsv
{
    /// <summary>As <see cref="PensionTransaction"/>'s columns.</summary>
    public const int MaxReference = 50, MaxDescription = 300, MaxFund = 200;

    private static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("en-GB");

    private static readonly string[] DateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];

    // "Some Fund Class I - Accumulation (GBP) 459.749 @  399.472300": the fund, then units @ price. Units sold to pay
    // a fee have a note between: "Some Fund … (GBP) Fee Sale - 1.234 @  399.472300".
    private static readonly Regex FundDeal = new(
        @"^(?<fund>.*?)(\s+(Fee\s+)?(Sale|Sell|Purchase|Buy))?(\s*-)?\s+[\d,]+(\.\d+)?\s+@\s+[\d,]+(\.\d+)?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static TransactionCsvResult Read(byte[] bytes)
    {
        var lines = Decode(bytes).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select((line, i) => (Row: i + 1, Cells: SplitLine(line)))
            .Where(l => l.Cells.Any(c => c.Length > 0))
            .ToList();
        if (lines.Count == 0) return Failed("The file is empty.");

        var movementsAt = lines.FindIndex(l => Column(l.Cells, "Date") >= 0 && Column(l.Cells, "Type") >= 0 && Column(l.Cells, "Total cost") >= 0);
        if (movementsAt >= 0) return ReadMovements(lines, movementsAt);

        var headingAt = lines.FindIndex(l => Column(l.Cells, "Trade date") >= 0 && Column(l.Cells, "Value") >= 0);
        if (headingAt < 0) return Failed("It has no heading row with Trade date and Value: is it a transaction history export?");

        var heading = lines[headingAt].Cells;
        int trade = Column(heading, "Trade date"), settle = Column(heading, "Settle date"), reference = Column(heading, "Reference"),
            description = Column(heading, "Description"), price = Column(heading, "Unit cost"), quantity = Column(heading, "Quantity"),
            value = Column(heading, "Value");

        var rows = new List<CsvTransaction>();
        var skipped = new List<string>();
        foreach (var (row, cells) in lines.Skip(headingAt + 1))
        {
            string Cell(int i) => i >= 0 && i < cells.Length ? cells[i] : string.Empty;
            var text = Cell(description);
            DateOnly tradeDate = default;
            DateOnly? settleDate = null;
            decimal amount = 0;
            decimal? unitPrice = null, units = null;
            var reason =
                !TryDate(Cell(trade), out tradeDate) ? $"the trade date \"{Cell(trade)}\" isn't a date"
                : !TryOptionalDate(Cell(settle), out settleDate) ? $"the settle date \"{Cell(settle)}\" isn't a date"
                : !TryAmount(Cell(value), out amount) ? $"the value \"{Cell(value)}\" isn't an amount"
                : !TryOptionalAmount(Cell(price), out unitPrice) ? $"the unit cost \"{Cell(price)}\" isn't an amount"
                : !TryOptionalAmount(Cell(quantity), out units) ? $"the quantity \"{Cell(quantity)}\" isn't an amount"
                : Cell(reference).Length > MaxReference ? $"the reference is over {MaxReference} characters"
                : text.Length > MaxDescription ? $"the description is over {MaxDescription} characters"
                : text.Length == 0 ? "it has no description"
                : null;
            if (reason is not null)
            {
                skipped.Add(string.Create(UkCulture, $"Line {row}: {reason}."));
                continue;
            }
            rows.Add(new CsvTransaction(row, tradeDate, settleDate, Cell(reference), text,
                Kind(Cell(reference), text, units, amount), Fund(text), unitPrice, units, amount));
        }
        return new TransactionCsvResult(null, rows, skipped);
    }

    /// <summary>
    /// What a line is, worked out from the provider's reference and description: a deal in a fund ("units @ price", with
    /// units) is a purchase (money out) or a sale (money in), whatever the fund's name says; otherwise a contribution,
    /// transfer in, interest, fee or rebate by their words, and any other line with units is a purchase or sale.
    /// </summary>
    public static TransactionKind Kind(string reference, string description, decimal? quantity, decimal value)
    {
        static bool Has(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
        if (quantity is { } units && units != 0 && FundDeal.IsMatch(description)) return value < 0 ? TransactionKind.Purchase : TransactionKind.Sale;
        if (reference.StartsWith("FPC", StringComparison.OrdinalIgnoreCase) || Has(description, "Contribution")) return TransactionKind.Contribution;
        if (Has(description, "Transfer") && value > 0) return TransactionKind.TransferIn;
        if (Has(reference, "INTEREST") || description.StartsWith("Interest", StringComparison.OrdinalIgnoreCase)) return TransactionKind.Interest;
        if (Has(reference, "FEE") || Has(description, " fee")) return TransactionKind.Fee;
        if (Has(description, "Rebate")) return TransactionKind.Rebate;
        if (quantity is { } q && q != 0) return value < 0 ? TransactionKind.Purchase : TransactionKind.Sale;
        return TransactionKind.Other;
    }

    /// <summary>
    /// The fund a purchase or sale is in: the description before its "units @ price", less a note such as "Fee Sale -".
    /// Null for other lines.
    /// </summary>
    public static string? Fund(string description)
    {
        var match = FundDeal.Match(description);
        if (!match.Success) return null;
        var fund = match.Groups["fund"].Value.Trim();
        return fund.Length is > 0 and <= MaxFund ? fund : null;
    }

    /// <summary>
    /// The lines to add, from one or more files, given the transactions the pension already has. Each transaction counts
    /// as many times as any one file has it, so files that overlap don't add it twice, while two identical lines in one
    /// file (it happens) both count; those the pension already has that many times are left out.
    /// </summary>
    public static List<CsvTransaction> NewRows(IEnumerable<IReadOnlyList<CsvTransaction>> files, IEnumerable<TransactionKey> existing)
    {
        var have = existing.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        var lines = new Dictionary<TransactionKey, List<CsvTransaction>>();
        foreach (var file in files)
            foreach (var group in file.GroupBy(r => r.Key))
            {
                var inFile = group.ToList();
                if (!lines.TryGetValue(group.Key, out var most) || inFile.Count > most.Count)
                    lines[group.Key] = inFile;
            }

        return lines
            .SelectMany(kv => kv.Value.Take(Math.Max(0, kv.Value.Count - have.GetValueOrDefault(kv.Key))))
            .OrderBy(r => r.TradeDate).ThenBy(r => r.Row)
            .ToList();
    }

    /// <summary>
    /// A security movements export's corporate actions and conversions. Values follow the history's sign (a cost added
    /// is negative, a cost taken off positive). A conversion's unit cost is its book cost per unit, not a price, so it's
    /// left out.
    /// </summary>
    private static TransactionCsvResult ReadMovements(List<(int Row, string[] Cells)> lines, int headingAt)
    {
        var title = lines.Take(headingAt).Select(l => l.Cells).FirstOrDefault(c => c[0].StartsWith("Security movements", StringComparison.OrdinalIgnoreCase));
        var fund = title is null ? string.Empty : string.Join(' ', title.Skip(1).Where(c => c.Length > 0));
        if (fund.Length == 0) return Failed("It has no first line saying which fund it's for.");
        if (fund.Length > MaxFund) return Failed($"Its fund's name is over {MaxFund} characters.");

        var heading = lines[headingAt].Cells;
        int date = Column(heading, "Date"), type = Column(heading, "Type"), reference = Column(heading, "Reference"),
            quantity = Column(heading, "Quantity"), cost = Column(heading, "Total cost");

        var rows = new List<CsvTransaction>();
        var skipped = new List<string>();
        var deals = 0;
        foreach (var (row, cells) in lines.Skip(headingAt + 1))
        {
            string Cell(int i) => i >= 0 && i < cells.Length ? cells[i] : string.Empty;
            var kind = Cell(type).ToLowerInvariant();
            if (kind is "bought" or "sold")
            {
                deals++;
                continue;
            }

            DateOnly on = default;
            decimal total = 0;
            decimal? units = null;
            var reason =
                kind is not ("corp.a" or "conversion in" or "conversion out") ? $"the type \"{Cell(type)}\" isn't one this reads"
                : !TryDate(Cell(date), out on) ? $"the date \"{Cell(date)}\" isn't a date"
                : !TryAmount(Cell(cost), out total) ? $"the total cost \"{Cell(cost)}\" isn't an amount"
                : !TryOptionalAmount(Cell(quantity), out units) ? $"the quantity \"{Cell(quantity)}\" isn't an amount"
                : Cell(reference).Length is 0 or > MaxReference ? $"the reference is missing or over {MaxReference} characters"
                : kind != "corp.a" && units is null or 0 ? "a conversion needs a quantity"
                : null;
            if (reason is not null)
            {
                skipped.Add(string.Create(UkCulture, $"Line {row}: {reason}."));
                continue;
            }

            if (kind == "corp.a")
            {
                if (total != 0)
                    rows.Add(new CsvTransaction(row, on, on, Cell(reference), "Cost adjustment (corporate action)", TransactionKind.CostAdjustment, fund, null, null, -total));
                continue;
            }
            var received = kind == "conversion in";
            rows.Add(new CsvTransaction(row, on, on, Cell(reference), received ? $"Converted into {fund}" : $"Converted out of {fund}",
                TransactionKind.Conversion, fund, null, received ? Math.Abs(units!.Value) : -Math.Abs(units!.Value), -total));
        }
        return new TransactionCsvResult(null, rows, skipped, deals);
    }

    /// <summary>UTF-8 if it is (with or without a byte order mark), else Windows-1252 as Excel saves it (£ is one byte).</summary>
    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>One CSV line's cells, trimmed: commas inside double quotes are kept, and "" is a quote.</summary>
    private static string[] SplitLine(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString().Trim());
        return [.. cells];
    }

    /// <summary>The first column whose heading starts with <paramref name="name"/> ("Value (£)" is Value), or -1.</summary>
    private static int Column(string[] cells, string name) =>
        Array.FindIndex(cells, c => c.StartsWith(name, StringComparison.OrdinalIgnoreCase));

    private static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, DateFormats, UkCulture, DateTimeStyles.None, out date);

    private static bool TryOptionalDate(string text, out DateOnly? date)
    {
        date = null;
        if (IsNone(text)) return true;
        if (!TryDate(text, out var d)) return false;
        date = d;
        return true;
    }

    private static bool TryAmount(string text, out decimal amount) =>
        decimal.TryParse(text.Replace("£", "").Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

    private static bool TryOptionalAmount(string text, out decimal? amount)
    {
        amount = null;
        if (IsNone(text)) return true;
        if (!TryAmount(text, out var a)) return false;
        amount = a;
        return true;
    }

    private static bool IsNone(string text) => text.Length == 0 || text.Equals("n/a", StringComparison.OrdinalIgnoreCase);

    private static TransactionCsvResult Failed(string error) => new(error, [], []);
}
