using System.Net;
using System.Text.RegularExpressions;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

public sealed record PageResponse(HttpStatusCode Status, string Html, string? Location)
{
    /// <summary>The page's text content, with HTML entities decoded (Razor encodes e.g. "+" as &amp;#x2B;).</summary>
    public string Text => WebUtility.HtmlDecode(Html);

    /// <summary>Validation and error messages shown on the page (the class can be anywhere in the attribute, e.g. after "alert").</summary>
    public string Errors => string.Join(" | ", Regex
        .Matches(Html, "class=\"[^\"]*\\b(?:text-danger|validation-summary-errors|field-validation-error)\\b[^\"]*\"[^>]*>(.*?)</(?!ul|li)", RegexOptions.Singleline)
        .Select(m => WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim())
        .Where(s => s.Length > 0));

    /// <summary>The first cell of each table row, in order (e.g. the names in a list page's table).</summary>
    public List<string> RowNames => Regex.Matches(Html, @"<tr[^>]*>\s*<td>([^<]*)</td>")
        .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

    /// <summary>The href of the first link with this title (e.g. "Edit") in the table row whose first cell is <paramref name="rowName"/>.</summary>
    public string? RowLink(string rowName, string title)
    {
        var row = Regex.Match(Html, $@"<tr>\s*<td>{Regex.Escape(WebUtility.HtmlEncode(rowName))}</td>(.*?)</tr>", RegexOptions.Singleline).Groups[1].Value;
        return Href(Regex.Match(row, $"<a [^>]*title=\"{Regex.Escape(title)}[^\"]*\"[^>]*>").Value);
    }

    /// <summary>The value of the option with this text in a select.</summary>
    public string OptionValue(string text) =>
        Regex.Match(Html, $"<option[^>]*value=\"([^\"]*)\"[^>]*>{Regex.Escape(WebUtility.HtmlEncode(text))}</option>").Groups[1].Value;

    public static string? Href(string tag) =>
        Regex.Match(tag, "href=\"([^\"]+)\"") is { Success: true } m ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
}
