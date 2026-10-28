using GreenHedges.HomeServer.Common.Heartbeat;

namespace GreenHedges.HomeServer.PayAndPensions.Monitoring;

/// <summary>
/// Whether the database has been answering, from the alert monitor's probes, for the alerts and the heartbeat. In memory,
/// so it starts empty after a restart. (Pay & Pensions has no scheduled jobs of its own; its backups are Common's, with
/// their own health.)
/// </summary>
public sealed class AppHealth(TimeProvider time) : IHeartbeatSource
{
    private readonly Lock _lock = new();
    private DateTimeOffset? _databaseOkAt;
    private DateTimeOffset? _databaseFailingSince;
    private string? _databaseError;

    public DateTimeOffset StartedAt { get; } = time.GetUtcNow();

    public void RecordDatabaseOk()
    {
        lock (_lock)
        {
            _databaseOkAt = time.GetUtcNow();
            _databaseFailingSince = null;
            _databaseError = null;
        }
    }

    public void RecordDatabaseFailure(string error)
    {
        lock (_lock)
        {
            _databaseFailingSince ??= time.GetUtcNow();
            _databaseError = error;
        }
    }

    /// <summary>Healthy for the heartbeat while the database answered within the interval.</summary>
    public string? HealthyStatus(DateTimeOffset now, TimeSpan interval)
    {
        var snapshot = Snapshot();
        return snapshot.DatabaseOkAt is { } ok && now - ok <= interval ? $"Database answered {ok:u}" : null;
    }

    public AppHealthSnapshot Snapshot()
    {
        lock (_lock) return new AppHealthSnapshot(StartedAt, _databaseOkAt, _databaseFailingSince, _databaseError);
    }
}

/// <param name="DatabaseOkAt">When the database last answered a probe, since startup.</param>
/// <param name="DatabaseFailingSince">When probes started failing; null while the database answers.</param>
public sealed record AppHealthSnapshot(DateTimeOffset StartedAt, DateTimeOffset? DatabaseOkAt, DateTimeOffset? DatabaseFailingSince, string? DatabaseError);
