using System.Text;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

/// <summary>
/// Made-up transaction exports, laid out as a provider's are: a few lines about the account, a heading row, then a
/// quoted line per transaction. Never a real export's names, numbers or figures.
/// </summary>
public static class TestCsv
{
    public const string Fund = "Example Growth Fund Class A - Accumulation (GBP)";

    public const string Heading = "Trade date,Settle date,Reference,Description,Unit cost (p),Quantity,Value (£)";

    /// <summary>A line: date, reference, description, unit cost, quantity, value, quoted as the exports are.</summary>
    public static string Line(string date, string reference, string description, string price, string quantity, string value) =>
        string.Join(",", new[] { date, date, reference, description, price, quantity, value }.Select(c => $"\"{c}\""));

    public static string Contribution(string date, string value) => Line(date, "FPC123", "SIPP Contribution ", "n/a", "n/a", value);

    public static string Purchase(string date, string reference, string units, string price, string value) =>
        Line(date, reference, $"{Fund} {units} @  {price}", price, units, value);

    public static string Interest(string date, string value) => Line(date, "INTEREST", "Interest From 01/01/2020 TO 31/01/2020", "n/a", "n/a", value);

    public static string Fee(string date, string value) => Line(date, "MANAGE FEE", "Example SIPP To A/C 000", "n/a", "n/a", value);

    /// <summary>The export, as Windows-1252 (as Excel and the provider save it: £ is one byte) unless <paramref name="utf8"/>.</summary>
    public static byte[] Export(IEnumerable<string> lines, bool utf8 = false)
    {
        var text = string.Join("\r\n", new[] { "Portfolio Summary", "Client Name:,Test Person", "Client Number:,000000", "Valuation as at:,01/01/2020", "", Heading }.Concat(lines)) + "\r\n";
        return utf8 ? Encoding.UTF8.GetBytes(text) : Encoding.Latin1.GetBytes(text);
    }

    public static byte[] Export(params string[] lines) => Export(lines, utf8: false);

    /// <summary>A movement line, as the provider writes them: unquoted, with a trailing comma.</summary>
    public static string Movement(string date, string type, string reference, string price, string quantity, string cost) =>
        $"{date},{type},{reference},{price},{quantity},{cost},";

    /// <summary>A fund's security movements export (the fund's name in two cells, as the provider splits it).</summary>
    public static byte[] Movements(string name, string share, params string[] lines) => Encoding.Latin1.GetBytes(string.Join("\r\n",
        new[] { $"Security movements for:, {name}, {share} , , ,", "Client Name: Test Person , , , , ,", "Client Number: 000000 , , , , ,", "",
            "Example SIPP , , , ,", "", "", "DATE, TYPE, REFERENCE, UNIT COST (p), QUANTITY, TOTAL COST (£)", "" }.Concat(lines)) + "\r\n");
}
