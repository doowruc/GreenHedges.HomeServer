using System.Text;
using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Collectors.Libre.LibreLinkUp;
using GreenHedges.HomeServer.Collectors.Libre.Nightscout;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Libre.Worker.Alerts;

/// <summary>
/// The Libre collector's checks: LibreLinkUp needing attention, stale readings, backups, Nightscout
/// publishing and off-site copies. Emails and their persistence are the shared <see cref="AlertBook"/>'s.
/// </summary>
public sealed class LibreAlertMonitor(
    CollectorHealth health,
    AlertBook alerts,
    IOptions<AlertOptions> alertOptions,
    IOptions<LibreAlertOptions> options,
    IOptions<NightscoutOptions> nightscoutOptions,
    BackupMonitor backups,
    TimeProvider time,
    ILogger<LibreAlertMonitor> logger) : AlertMonitorService(alerts, alertOptions, time, logger)
{
    private static readonly string[] Keys = Enum.GetNames<AlertKind>();

    private readonly LibreAlertOptions _options = options.Value;
    private readonly NightscoutOptions _nightscoutOptions = nightscoutOptions.Value;

    private enum AlertKind
    {
        /// <summary>LibreLinkUp needs the user to act (credentials, terms, app version, sharing).</summary>
        UserAction,

        /// <summary>
        /// No new current reading for longer than <see cref="LibreAlertOptions.StaleAfter"/>, or for that
        /// long after a new sensor's warm-up was due to end.
        /// </summary>
        Stale,

        /// <summary>No good backup for longer than <see cref="BackupOptions.AlertAfter"/> (checked by <see cref="BackupMonitor"/>).</summary>
        Backup,

        /// <summary>Nightscout publishing failing for longer than <see cref="LibreAlertOptions.PublishStalledAfter"/>.</summary>
        Publish,

        /// <summary>No successful off-site copy for longer than <see cref="OffsiteOptions.AlertAfter"/> (checked by <see cref="BackupMonitor"/>).</summary>
        Offsite,
    }

    protected override IReadOnlyCollection<string> AlertKeys => Keys;

    public override async Task CheckAsync(CancellationToken cancellationToken)
    {
        var snapshot = health.Snapshot();
        var now = Time.GetUtcNow();

        if (snapshot.LastError is LibreLinkUpUserActionRequiredException userAction)
        {
            await RaiseAsync(
                AlertKind.UserAction,
                "LibreLinkUp needs attention",
                $"""
                Collection is paused (it retries periodically) until this is fixed:

                {userAction.Message}

                {Describe(snapshot, now)}
                """,
                cancellationToken);
        }
        else if (snapshot.LastSuccessAt is not null)
        {
            await ResolveAsync(AlertKind.UserAction, cancellationToken);
        }

        // Measured from startup too, so a restart gets a grace period rather than an instant alert.
        var latest = snapshot.LatestReadingAt is { } readingAt && readingAt > snapshot.StartedAt
            ? readingAt
            : snapshot.StartedAt;

        // A sensor activated after the newest reading is warming up: no readings are due until it's ready.
        var warmingUp = snapshot.Sensor is { ActivatedAt: { } activated, ReadyAt: { } readyAt }
                        && (snapshot.LatestReadingAt is not { } newest || activated > newest)
                        && readyAt > latest;
        var expectedFrom = warmingUp ? snapshot.Sensor!.ReadyAt!.Value : latest;

        if (now - expectedFrom > _options.StaleAfter)
        {
            // While a user-action alert is open it already explains the missing data.
            if (!Alerts.IsOpen(nameof(AlertKind.UserAction)))
            {
                await RaiseAsync(
                    AlertKind.Stale,
                    "No new glucose readings",
                    $"""
                    No new glucose reading for {FormatDuration(now - latest)}.{DescribeSensorState(snapshot.Sensor, warmingUp, now)}

                    {Describe(snapshot, now)}
                    """,
                    cancellationToken);
            }
        }
        else if (snapshot.LatestReadingAt is { } fresh && now - fresh <= _options.StaleAfter)
        {
            // A real recent reading, not just the startup grace period.
            await ResolveAsync(AlertKind.Stale, cancellationToken);
        }

        await backups.CheckAsync(Alerts, now, Describe(snapshot, now), cancellationToken);

        if (snapshot.PublishFailingSince is { } failingSince && now - failingSince > _options.PublishStalledAfter)
        {
            await RaiseAsync(
                AlertKind.Publish,
                "Nightscout publishing stalled",
                $"""
                Publishing to Nightscout has been failing for {FormatDuration(now - failingSince)}.
                Readings are still being collected and archived; Nightscout will catch up once it's reachable.
                Last error: {(snapshot.LastPublishError is { } e ? $"{e.GetType().Name}: {e.Message}" : "none recorded")}

                {Describe(snapshot, now)}
                """,
                cancellationToken);
        }
        else if (!_nightscoutOptions.Enabled
                 || (snapshot.PublishFailingSince is null && snapshot.LastPublishSuccessAt is not null))
        {
            await ResolveAsync(AlertKind.Publish, cancellationToken);
        }
    }

    private Task RaiseAsync(AlertKind kind, string title, string body, CancellationToken cancellationToken) =>
        Alerts.RaiseAsync(kind.ToString(), title, body, cancellationToken);

    private Task ResolveAsync(AlertKind kind, CancellationToken cancellationToken) =>
        Alerts.ResolveAsync(kind.ToString(), cancellationToken);

    /// <summary>Why the sensor might explain the missing readings: each line preceded by a newline, or empty.</summary>
    private static string DescribeSensorState(SensorStatus? sensor, bool warmingUp, DateTimeOffset now)
    {
        var text = new StringBuilder();
        if (warmingUp && sensor is { ActivatedAt: { } activated, ReadyAt: { } readyAt })
        {
            text.Append($"\nNew sensor {sensor.Serial} was activated {activated:u}; its readings were expected from {readyAt:u}.");
        }

        if (sensor is { ExpectedEnd: { } end } && now >= end)
        {
            text.Append($"\nSensor {sensor.Serial} reached its expected end at {end:u}; it has probably expired.");
        }

        return text.ToString();
    }

    private static string Describe(CollectorHealthSnapshot snapshot, DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.AppendLine($"Host: {Environment.MachineName}");
        text.AppendLine($"Now: {now:u}");
        text.AppendLine($"Collector started: {snapshot.StartedAt:u}");
        text.AppendLine($"Newest reading (sensor time): {(snapshot.LatestReadingAt is { } r ? $"{r:u}" : "none since startup")}");
        text.AppendLine($"Last successful poll: {(snapshot.LastSuccessAt is { } s ? $"{s:u}" : "none since startup")}");
        if (snapshot.Sensor is { } sensor)
        {
            text.AppendLine(
                $"Sensor: {sensor.Serial}, activated {(sensor.ActivatedAt is { } a ? $"{a:u}" : "unknown")}, expected end {(sensor.ExpectedEnd is { } e ? $"{e:u}" : "unknown")}");
        }

        if (snapshot.LastError is { } error)
        {
            text.AppendLine($"Last error ({snapshot.LastErrorAt:u}): {error.GetType().Name}: {error.Message}");
        }

        return text.ToString();
    }

    private static string FormatDuration(TimeSpan duration) => AlertBook.FormatDuration(duration);
}
