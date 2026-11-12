namespace GreenHedges.HomeServer.Utilities.Monitoring;

/// <summary>Utilities' alert thresholds, bound from the shared <c>Alerts</c> section.</summary>
public sealed class UtilitiesAlertOptions
{
    /// <summary>Alert if the database hasn't answered for this long (probed every <c>Alerts:CheckInterval</c>).</summary>
    public TimeSpan DatabaseDownAfter { get; set; } = TimeSpan.FromMinutes(5);
}
