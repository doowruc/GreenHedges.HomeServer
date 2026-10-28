using System.Diagnostics;
using System.Text.RegularExpressions;
using GreenHedges.HomeServer.Common.Web.Hosting;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// The app's OpenTelemetry, set up by Common.Web's <see cref="WebAppTelemetry"/> (sent over OTLP to the server's
/// telemetry stack and Grafana when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set), plus what's particular to Vehicle
/// Manager: its own spans, and the DVSA's request URLs (which end in the registration) redacted in traces
/// (<see cref="RedactUrl"/>).
/// </summary>
public static partial class Telemetry
{
    public const string ServiceName = "VehicleManager";

    /// <summary>The app's own spans, e.g. one per nightly job run and per user within it.</summary>
    public static readonly ActivitySource Source = new(ServiceName);

    public static void AddTelemetry(this WebApplicationBuilder builder) => builder.AddTelemetry(ServiceName, RedactUrl);

    /// <summary>A request URL as traces record it, with the registration at the end of a DVSA lookup replaced by "{registration}".</summary>
    public static string RedactUrl(Uri url) => RegistrationPath().Replace(url.AbsoluteUri, "$1{registration}");

    [GeneratedRegex(@"(/vehicles/registration/)[^/?#]+", RegexOptions.IgnoreCase)]
    private static partial Regex RegistrationPath();
}
