using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using GreenHedges.HomeServer.PayAndPensions.Services;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Unit;

public class EodhdPriceSourceTests
{
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static EodhdPriceSource Source(HttpMessageHandler handler, string? token = "made up") =>
        new(new HttpClient(handler), Options.Create(new EodhdOptions { ApiToken = token }));

    [Fact]
    public async Task It_asks_by_isin_from_a_date_and_reads_the_closing_prices()
    {
        var handler = new Handler(HttpStatusCode.OK, """[{"date":"2020-06-01","open":1,"high":1,"low":1,"close":100.5,"adjusted_close":100.5,"volume":0}]""");

        var prices = await Source(handler).GetAsync("GB0000000001", new DateOnly(2020, 6, 1), CancellationToken.None);

        Assert.Equal([new SourcePrice(new DateOnly(2020, 6, 1), 100.5m)], prices);
        Assert.Equal("https://eodhd.com/api/eod/GB0000000001.EUFUND?api_token=made%20up&fmt=json&from=2020-06-01", handler.Requested!.AbsoluteUri);
    }

    [Fact]
    public async Task A_failed_request_throws()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Source(new Handler(HttpStatusCode.NotFound, "")).GetAsync("GB0000000001", new DateOnly(2020, 6, 1), CancellationToken.None));
    }

    [Theory]
    [InlineData("made up", true)]
    [InlineData("  ", false)]
    [InlineData(null, false)]
    public void It_needs_a_key(string? token, bool configured) =>
        Assert.Equal(configured, Source(new Handler(HttpStatusCode.OK, "[]"), token).IsConfigured);

    [Fact]
    public void Only_its_own_urls_lose_their_query_in_traces()
    {
        Assert.Equal("https://eodhd.com/api/eod/X.EUFUND", EodhdPriceSource.Redact(new Uri("https://eodhd.com/api/eod/X.EUFUND?api_token=secret&fmt=json")));
        Assert.Equal("https://example.com/a?b=c", EodhdPriceSource.Redact(new Uri("https://example.com/a?b=c")));
    }

    [Theory]
    [InlineData("2026-01-15T07:00:00Z", "2026-01-15T08:00:00+00:00")]   // winter: UK time is UTC
    [InlineData("2026-01-15T08:00:00Z", "2026-01-16T08:00:00+00:00")]   // at the time: tomorrow
    [InlineData("2026-07-15T06:30:00Z", "2026-07-15T08:00:00+01:00")]   // summer: an hour ahead
    [InlineData("2026-07-15T07:30:00Z", "2026-07-16T08:00:00+01:00")]
    public void The_job_runs_each_morning_uk_time(string now, string next) =>
        Assert.Equal(DateTimeOffset.Parse(next, System.Globalization.CultureInfo.InvariantCulture),
            FundPriceJob.NextRun(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));
}
