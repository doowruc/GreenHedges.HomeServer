using GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker;

/// <summary>
/// Tells a LibreLinkUp blip from lost sharing. LibreLinkUp occasionally returns an empty connections
/// list for a single poll, so an empty list is a transient error until it has lasted <paramref name="grace"/>,
/// and only then a <see cref="NoConnectionsException"/> (which needs the user and raises an alert).
/// </summary>
internal sealed class NoConnectionsGrace(TimeSpan grace)
{
    private DateTimeOffset? _emptySince;

    /// <summary>Connections came back, so the next empty list starts a new grace period.</summary>
    public void Reset() => _emptySince = null;

    /// <returns>The exception to throw for an empty connections list seen at <paramref name="now"/>.</returns>
    public LibreLinkUpException Empty(DateTimeOffset now)
    {
        _emptySince ??= now;
        var elapsed = now - _emptySince.Value;
        return elapsed >= grace
            ? new NoConnectionsException()
            : new LibreLinkUpException(
                $"LibreLinkUp returned no connections (for {elapsed:hh\\:mm\\:ss}); treated as transient until it lasts {grace:hh\\:mm\\:ss}.");
    }
}
