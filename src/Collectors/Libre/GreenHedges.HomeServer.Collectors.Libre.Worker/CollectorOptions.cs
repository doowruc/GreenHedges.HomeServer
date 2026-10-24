namespace GreenHedges.HomeServer.Collectors.Libre.Worker;

public sealed class CollectorOptions
{
    public const string SectionName = "Collector";

    /// <summary>
    /// Which LibreLinkUp connection to collect. Optional when the follower account has one connection.
    /// </summary>
    public string? PatientId { get; set; }

    /// <summary>How often to poll <c>/llu/connections</c> for the current reading.</summary>
    public TimeSpan CurrentInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Poll this long after the latest reading's sensor time (then every <see cref="CurrentInterval"/>).
    /// Readings appear a few seconds after their sensor time and are replaced one interval later,
    /// so about halfway through gives the most slack either side.
    /// </summary>
    public TimeSpan PollOffset { get; set; } = TimeSpan.FromSeconds(35);

    /// <summary>Sensor wear time, for a sensor's expected end in logs and alerts (Libre 2 Plus: 15 days).</summary>
    public TimeSpan SensorLifetime { get; set; } = TimeSpan.FromDays(15);

    /// <summary>Sensor warm-up, used when LibreLinkUp doesn't report one (<c>w</c>). Libre 2 Plus: 60 minutes.</summary>
    public TimeSpan SensorWarmup { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>How often to poll <c>/graph</c> to fill gaps (about 12 hours at 15-minute resolution).</summary>
    public TimeSpan GraphInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Wait between transient failures. Doubles on each consecutive failure, up to the maximum.</summary>
    public TimeSpan FailureBackoff { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaxFailureBackoff { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Pause after an error that needs the user to act (bad password, terms to accept, version too old).
    /// Kept long so repeated logins don't lock the account.
    /// </summary>
    public TimeSpan UserActionPause { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Minimum pause after an HTTP 429, even if <c>Retry-After</c> is shorter or missing.</summary>
    public TimeSpan MinRateLimitPause { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed class ArchiveOptions
{
    public const string SectionName = "Archive";

    /// <summary>
    /// SQLite file path. A relative path is resolved against the working directory, like the log path.
    /// As a Windows Service the working directory is System32, so use an absolute path there.
    /// </summary>
    public string DatabasePath { get; set; } = "librecollector.db";
}
