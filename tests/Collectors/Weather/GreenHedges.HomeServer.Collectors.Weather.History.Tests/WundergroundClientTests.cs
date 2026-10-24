using System.Net;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Weather.History.Tests;

public sealed class WundergroundClientTests
{
    private readonly FakeWunderground _api = new();

    private WundergroundClient Client() => new(
        new HttpClient(_api),
        Options.Create(new WundergroundOptions { StationId = "ISTATION1", ApiKey = "secret-key", BaseUrl = "https://api.example.test/" }));

    [Fact]
    public async Task Asks_for_one_local_day_in_metric_units_and_keeps_the_body_as_received()
    {
        var json = FakeWunderground.Json((1792195494, 0.4));
        _api.Day("20261018", json);

        var response = await Client().FetchDayAsync(new DateOnly(2026, 10, 18), CancellationToken.None);

        var url = Assert.Single(_api.Requests);
        Assert.Equal("https://api.example.test/v2/pws/history/all", url.GetLeftPart(UriPartial.Path));
        Assert.Contains("stationId=ISTATION1", url.Query);
        Assert.Contains("units=m", url.Query);
        Assert.Contains("date=20261018", url.Query);
        Assert.Equal(json, response.RawJson);
        var o = Assert.Single(response.Observations);
        Assert.Equal(1792195494, o.Epoch);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792195494), o.ObsTimeUtc);
        Assert.Equal((10.5, 11.0, 10.0), (o.TempAvgC, o.TempHighC, o.TempLowC));
        Assert.Equal((90.0, 92.0, 88.0), (o.HumidityAvg, o.HumidityHigh, o.HumidityLow));
        Assert.Equal((7.2, 14.4, 225.0), (o.WindAvgKmh, o.GustHighKmh, o.WindDirAvgDeg));
        Assert.Equal((1012.4, 1012.0, 0.4, 1), (o.PressureMaxHpa, o.PressureMinHpa, o.PrecipTotalMm, o.QcStatus));
    }

    [Fact]
    public async Task A_day_without_data_is_empty()
    {
        var response = await Client().FetchDayAsync(new DateOnly(2021, 11, 1), CancellationToken.None);

        Assert.Equal("", response.RawJson);
        Assert.Empty(response.Observations);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_key_is_an_auth_error_that_never_shows_the_key(HttpStatusCode status)
    {
        _api.Status("20261018", status);

        var error = await Assert.ThrowsAsync<WundergroundAuthException>(() => Client().FetchDayAsync(new DateOnly(2026, 10, 18), CancellationToken.None));

        Assert.DoesNotContain("secret-key", error.Message);
    }

    [Fact]
    public async Task The_rate_limit_is_its_own_error_and_other_failures_never_show_the_key()
    {
        _api.Status("20261018", HttpStatusCode.TooManyRequests).Status("20261019", HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<WundergroundRateLimitException>(() => Client().FetchDayAsync(new DateOnly(2026, 10, 18), CancellationToken.None));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Client().FetchDayAsync(new DateOnly(2026, 10, 19), CancellationToken.None));
        Assert.DoesNotContain("secret-key", error.Message);
    }

    [Fact]
    public void Observations_without_an_epoch_and_missing_values_are_handled()
    {
        var observations = WundergroundClient.Parse("""{"observations":[{"obsTimeUtc":"2026-10-18T00:04:54Z"},{"epoch":1792195494,"metric":{"tempAvg":null}}]}""");

        var o = Assert.Single(observations);
        Assert.Null(o.TempAvgC);
        Assert.Null(o.PrecipTotalMm);
        Assert.Empty(WundergroundClient.Parse("""{"observations":null}"""));
    }
}
