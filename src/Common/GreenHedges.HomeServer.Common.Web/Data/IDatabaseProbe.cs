namespace GreenHedges.HomeServer.Common.Web.Data;

/// <summary>Asks whether the database answers, for the alert checks.</summary>
public interface IDatabaseProbe
{
    /// <summary>Null if the database answered, else what went wrong. Never throws (except on shutdown).</summary>
    Task<string?> ProbeAsync(CancellationToken cancellationToken);
}
