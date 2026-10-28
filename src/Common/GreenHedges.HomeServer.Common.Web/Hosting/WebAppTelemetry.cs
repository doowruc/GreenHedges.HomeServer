using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GreenHedges.HomeServer.Common.Web.Hosting;

/// <summary>
/// OpenTelemetry logs, traces and metrics, sent over OTLP when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set: on the server
/// to its telemetry stack (Alloy, into Loki, Tempo and Prometheus, shown in Grafana; <c>deploy/telemetry/</c>). The
/// standard <c>OTEL_*</c> settings (endpoint, headers, protocol, service name) configure it, so the destination can change
/// without code. Without an endpoint (plain <c>dotnet watch</c>, the tests) nothing is set up and logs go only to the
/// console (and the journal on the server).
/// </summary>
/// <remarks>
/// Nothing personal may go out: logs carry Auth0 user ids, never emails, names or amounts; SQL commands are traced as
/// their parameterised text, never the values; and an app whose outgoing request URLs hold personal data passes
/// <c>redactUrl</c>.
/// </remarks>
public static class WebAppTelemetry
{
    public static bool IsExporting(IConfiguration configuration) =>
        configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 };

    /// <param name="serviceName">The app's name in Grafana (unless <c>OTEL_SERVICE_NAME</c> says otherwise), and the
    /// name of its own <see cref="System.Diagnostics.ActivitySource"/>, whose spans are included.</param>
    /// <param name="redactUrl">How an outgoing request's URL is recorded in traces, if not as it is.</param>
    public static void AddTelemetry(this WebApplicationBuilder builder, string serviceName, Func<Uri, string>? redactUrl = null)
    {
        if (!IsExporting(builder.Configuration)) return;

        // Through ILogger, so every existing log call is included.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? serviceName))
            .WithTracing(tracing => tracing
                .AddSource(serviceName)
                // Pages and form posts, not static files (css, js, images).
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !Path.HasExtension(context.Request.Path.Value))
                .AddHttpClientInstrumentation(options => options.EnrichWithHttpRequestMessage = (activity, request) =>
                {
                    if (redactUrl is not null && request.RequestUri is { } url && redactUrl(url) is var redacted && redacted != url.AbsoluteUri)
                        activity.SetTag("url.full", redacted);
                })
                // SQL Server, with EF Core's parameterised commands (the text, not the values).
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .UseOtlpExporter();
    }
}
