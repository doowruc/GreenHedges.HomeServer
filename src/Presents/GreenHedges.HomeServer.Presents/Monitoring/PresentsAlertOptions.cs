namespace GreenHedges.HomeServer.Presents.Monitoring;

/// <summary>Presents' alert thresholds, bound from the shared <c>Alerts</c> section.</summary>
public sealed class PresentsAlertOptions
{
    /// <summary>Alert if the database hasn't answered for this long (probed every <c>Alerts:CheckInterval</c>).</summary>
    public TimeSpan DatabaseDownAfter { get; set; } = TimeSpan.FromMinutes(5);
}
