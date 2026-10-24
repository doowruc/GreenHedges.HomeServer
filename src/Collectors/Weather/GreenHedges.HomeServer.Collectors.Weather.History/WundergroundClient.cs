using System.Globalization;
using System.Net;
using System.Text.Json;
using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Weather.History;

/// <summary>Weather Underground refused the API key (HTTP 401 or 403).</summary>
public sealed class WundergroundAuthException(string message) : Exception(message);

/// <summary>Weather Underground's rate limit was reached (HTTP 429): stop for now.</summary>
public sealed class WundergroundRateLimitException(string message) : Exception(message);

/// <summary>One day's response: the body exactly as received (empty for a day without data) and its observations.</summary>
public sealed record WundergroundResponse(string RawJson, IReadOnlyList<WundergroundObservation> Observations);

/// <summary>
/// The PWS history API (<c>/v2/pws/history/all</c>): every observation of one local day, metric units. A day without
/// data comes back as 204 No Content.
/// </summary>
public sealed class WundergroundClient(HttpClient http, IOptions<WundergroundOptions> options)
{
    private readonly WundergroundOptions _options = options.Value;

    public async Task<WundergroundResponse> FetchDayAsync(DateOnly localDate, CancellationToken cancellationToken)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/v2/pws/history/all?stationId={Uri.EscapeDataString(_options.StationId)}" +
            $"&format=json&units=m&numericPrecision=decimal&date={localDate:yyyyMMdd}&apiKey={Uri.EscapeDataString(_options.ApiKey)}";
        using var response = await http.GetAsync(url, cancellationToken);

        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent:
                return new WundergroundResponse("", []);
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new WundergroundAuthException(
                    $"Weather Underground refused the API key ({(int)response.StatusCode}). Check Wunderground:ApiKey and Wunderground:StationId.");
            case HttpStatusCode.TooManyRequests:
                throw new WundergroundRateLimitException("Weather Underground's rate limit was reached (429).");
        }

        // Never put the URL (it holds the key) in an exception.
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Weather Underground answered {(int)response.StatusCode} {response.ReasonPhrase} for {localDate:yyyy-MM-dd}.", null, response.StatusCode);
        }

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return new WundergroundResponse(raw, Parse(raw));
    }

    /// <summary>Reads a <c>history/all</c> response (metric units).</summary>
    internal static IReadOnlyList<WundergroundObservation> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("observations", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var observations = new List<WundergroundObservation>();
        foreach (var o in items.EnumerateArray())
        {
            if (Number(o, "epoch") is not { } epoch)
            {
                continue;
            }

            var metric = o.TryGetProperty("metric", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
            observations.Add(new WundergroundObservation(
                (long)epoch,
                o.TryGetProperty("obsTimeUtc", out var t) && t.ValueKind == JsonValueKind.String
                    ? DateTimeOffset.Parse(t.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime()
                    : DateTimeOffset.FromUnixTimeSeconds((long)epoch),
                Number(metric, "tempAvg"), Number(metric, "tempHigh"), Number(metric, "tempLow"),
                Number(o, "humidityAvg"), Number(o, "humidityHigh"), Number(o, "humidityLow"),
                Number(metric, "dewptAvg"),
                Number(metric, "windspeedAvg"), Number(metric, "windspeedHigh"), Number(metric, "windgustHigh"),
                Number(o, "winddirAvg"),
                Number(metric, "pressureMax"), Number(metric, "pressureMin"),
                Number(metric, "precipRate"), Number(metric, "precipTotal"),
                Number(o, "qcStatus") is { } qc ? (int)qc : null));
        }

        return observations;
    }

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
