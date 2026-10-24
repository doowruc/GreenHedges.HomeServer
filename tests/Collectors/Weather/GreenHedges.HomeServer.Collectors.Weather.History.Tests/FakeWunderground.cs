using System.Net;
using System.Web;

namespace GreenHedges.HomeServer.Collectors.Weather.History.Tests;

/// <summary>Answers Weather Underground history calls: a day's observations, by local date, or a status code.</summary>
internal sealed class FakeWunderground : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _days = [];

    public List<string> Dates { get; } = [];

    public List<Uri> Requests { get; } = [];

    /// <summary>What any day not set up answers: 204 No Content (a day without data).</summary>
    public HttpStatusCode Default { get; set; } = HttpStatusCode.NoContent;

    public FakeWunderground Day(string yyyyMMdd, string json)
    {
        _days[yyyyMMdd] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        return this;
    }

    public FakeWunderground Status(string yyyyMMdd, HttpStatusCode status)
    {
        _days[yyyyMMdd] = () => new HttpResponseMessage(status);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        var date = HttpUtility.ParseQueryString(request.RequestUri!.Query)["date"]!;
        Dates.Add(date);
        return Task.FromResult(_days.TryGetValue(date, out var reply) ? reply() : new HttpResponseMessage(Default));
    }

    /// <summary>A minimal <c>history/all</c> response with one observation per (epoch, precipTotal).</summary>
    public static string Json(params (long Epoch, double PrecipTotal)[] observations) =>
        "{\"observations\":[" + string.Join(",", observations.Select(o =>
            "{\"stationID\":\"ISTATION1\",\"tz\":\"Europe/London\",\"obsTimeUtc\":\"" +
            DateTimeOffset.FromUnixTimeSeconds(o.Epoch).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture) +
            "\",\"epoch\":" + o.Epoch +
            ",\"humidityHigh\":92,\"humidityLow\":88,\"humidityAvg\":90,\"winddirAvg\":225,\"qcStatus\":1,\"metric\":{\"tempHigh\":11.0," +
            "\"tempLow\":10.0,\"tempAvg\":10.5,\"windspeedHigh\":9.0,\"windspeedAvg\":7.2,\"windgustHigh\":14.4,\"dewptAvg\":8.9," +
            "\"pressureMax\":1012.4,\"pressureMin\":1012.0,\"precipRate\":0.0,\"precipTotal\":" +
            o.PrecipTotal.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}")) + "]}";
}
