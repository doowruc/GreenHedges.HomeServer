namespace GreenHedges.HomeServer.Collectors.Weather.Worker.Alerts;

/// <summary>The Weather collector's alert thresholds, bound from the shared <c>Alerts</c> section.</summary>
public sealed class WeatherAlertOptions
{
    /// <summary>Alert if nothing has arrived from the station's ids for this long (it sends every 12 s).</summary>
    public TimeSpan StationSilentAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Alert if this many rtl_433 runs ended (crashed, exited, were killed or didn't start) in the last hour.</summary>
    public int ReceiverEndsPerHour { get; set; } = 3;

    /// <summary>Alert if the station's <c>battery_ok</c> has been 0 for this long.</summary>
    public TimeSpan BatteryLowAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Alert if publishing minute summaries (to SQL Server, for Grafana) has been failing for this long.</summary>
    public TimeSpan PublishStalledAfter { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Alert if Weather Underground history runs have been failing for this long (one is retried hourly).</summary>
    public TimeSpan WundergroundFailingAfter { get; set; } = TimeSpan.FromHours(6);
}
