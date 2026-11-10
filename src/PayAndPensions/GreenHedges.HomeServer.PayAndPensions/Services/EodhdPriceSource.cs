using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.PayAndPensions.Services;

/// <summary>The feed's settings: off until <see cref="ApiToken"/> is set (Eodhd__ApiToken on the server).</summary>
public sealed class EodhdOptions
{
    public const string SectionName = "Eodhd";

    public string? ApiToken { get; set; }
}

/// <summary>
/// End-of-day fund prices from EODHD, by ISIN, and its search for finding a fund's ISIN by name. Its API takes the key in the URL, so a request's URL must never be logged
/// or traced: <see cref="Redact"/> is for the traces, and Program turns off this client's request logging.
/// </summary>
public sealed class EodhdPriceSource(HttpClient http, IOptions<EodhdOptions> options) : IFundPriceSource
{
    private const string Host = "eodhd.com";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiToken);

    public async Task<IReadOnlyList<SourcePrice>> GetAsync(string isin, DateOnly from, CancellationToken cancellationToken)
    {
        var url = $"https://{Host}/api/eod/{Uri.EscapeDataString(isin)}.EUFUND?api_token={Uri.EscapeDataString(options.Value.ApiToken!)}" +
            $"&fmt=json&from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<Row>>(cancellationToken) ?? [];
        return rows.Select(r => new SourcePrice(r.Date, r.Close)).ToList();
    }

    /// <summary>The search's limit: a page of matches to pick from.</summary>
    public const int MaxMatches = 20;

    public async Task<IReadOnlyList<FundMatch>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var url = $"https://{Host}{SearchPath}{Uri.EscapeDataString(query)}?api_token={Uri.EscapeDataString(options.Value.ApiToken!)}" +
            $"&fmt=json&type=fund&limit={MaxMatches}";
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<Match>>(cancellationToken) ?? [];
        return rows.Where(r => !string.IsNullOrWhiteSpace(r.Isin) && !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new FundMatch(r.Name!.Trim(), r.Isin!.Trim().ToUpperInvariant(), r.Exchange, r.Currency, r.PreviousClose, r.PreviousCloseDate))
            .DistinctBy(m => m.Isin)
            .ToList();
    }

    private const string SearchPath = "/api/search/";

    /// <summary>
    /// A request's URL as it may be recorded: this feed's without its query, which holds the key, and a search's
    /// without what was searched for (a fund someone holds).
    /// </summary>
    public static string Redact(Uri url) =>
        !url.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) ? url.AbsoluteUri
        : url.AbsolutePath.StartsWith(SearchPath, StringComparison.OrdinalIgnoreCase) ? $"https://{Host}{SearchPath}"
        : url.GetLeftPart(UriPartial.Path);

    private sealed record Match(
        [property: JsonPropertyName("Name")] string? Name, [property: JsonPropertyName("ISIN")] string? Isin,
        [property: JsonPropertyName("Exchange")] string? Exchange, [property: JsonPropertyName("Currency")] string? Currency,
        [property: JsonPropertyName("previousClose")] decimal? PreviousClose, [property: JsonPropertyName("previousCloseDate")] string? PreviousCloseDate);

    private sealed record Row([property: JsonPropertyName("date")] DateOnly Date, [property: JsonPropertyName("close")] decimal Close);
}
