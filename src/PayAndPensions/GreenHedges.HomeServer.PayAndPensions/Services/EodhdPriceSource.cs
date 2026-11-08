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
/// End-of-day fund prices from EODHD, by ISIN. Its API takes the key in the URL, so a request's URL must never be logged
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

    /// <summary>A request's URL as it may be recorded: this feed's without its query, which holds the key.</summary>
    public static string Redact(Uri url) => url.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) ? url.GetLeftPart(UriPartial.Path) : url.AbsoluteUri;

    private sealed record Row([property: JsonPropertyName("date")] DateOnly Date, [property: JsonPropertyName("close")] decimal Close);
}
