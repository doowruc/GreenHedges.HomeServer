using Microsoft.Extensions.Configuration;
using GreenHedges.HomeServer.Common.Web.Hosting;

namespace GreenHedges.HomeServer.Common.Web.Tests;

public class WebAppTelemetryTests
{
    [Fact]
    public void Telemetry_is_only_sent_with_an_endpoint()
    {
        static IConfiguration Settings(string? endpoint) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint }).Build();
        Assert.False(WebAppTelemetry.IsExporting(Settings(null)));
        Assert.False(WebAppTelemetry.IsExporting(Settings("")));
        Assert.True(WebAppTelemetry.IsExporting(Settings("http://127.0.0.1:4317")));
    }
}
