using System.Text;
using Microsoft.Extensions.Options;
using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.VehicleManager.Monitoring;

/// <summary>
/// Vehicle Manager's checks, every <c>Alerts:CheckInterval</c>: the database answering, the nightly emailed backups and
/// morning reminders completing without failures, and (through <see cref="BackupMonitor"/>) the nightly database
/// backups and their off-site copies. Emails and their persistence are the shared <see cref="AlertBook"/>'s.
/// </summary>
/// <remarks>
/// <see cref="AppHealth"/> starts empty after a restart while open alerts are restored, so alerts resolve only on
/// positive evidence since startup: the database answering, or a later run without failures.
/// </remarks>
public sealed class VehicleManagerAlertMonitor(
    AppHealth health,
    IDatabaseProbe database,
    BackupMonitor backups,
    AlertBook alerts,
    IOptions<AlertOptions> alertOptions,
    IOptions<VehicleManagerAlertOptions> options,
    TimeProvider time,
    ILogger<VehicleManagerAlertMonitor> logger) : AlertMonitorService(alerts, alertOptions, time, logger)
{
    private static readonly string[] Keys = [.. Enum.GetNames<AlertKind>(), .. BackupMonitor.Keys];

    private readonly VehicleManagerAlertOptions _options = options.Value;

    private enum AlertKind
    {
        /// <summary>The database hasn't answered for <see cref="VehicleManagerAlertOptions.DatabaseDownAfter"/>.</summary>
        Database,

        /// <summary>The latest nightly backup run had failures, or none has completed for <see cref="VehicleManagerAlertOptions.JobOverdueAfter"/>.</summary>
        NightlyBackups,

        /// <summary>The same, for the morning reminders.</summary>
        MorningReminders,
    }

    protected override IReadOnlyCollection<string> AlertKeys => Keys;

    public override async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (await database.ProbeAsync(cancellationToken) is { } problem)
            health.RecordDatabaseFailure(problem);
        else
            health.RecordDatabaseOk();

        var snapshot = health.Snapshot();
        var now = Time.GetUtcNow();
        var context = Describe(snapshot, now);

        await CheckDatabaseAsync(snapshot, now, context, cancellationToken);

        // While the database is failing (alerted yet or not) it explains any job failures; they're raised once it's back
        // if they still apply.
        if (snapshot.DatabaseFailingSince is null)
        {
            await CheckJobAsync(ScheduledJob.NightlyBackups, AlertKind.NightlyBackups, snapshot, now, context, cancellationToken);
            await CheckJobAsync(ScheduledJob.MorningReminders, AlertKind.MorningReminders, snapshot, now, context, cancellationToken);
        }

        await backups.CheckAsync(Alerts, now, context, cancellationToken);
    }

    private async Task CheckDatabaseAsync(AppHealthSnapshot snapshot, DateTimeOffset now, string context, CancellationToken cancellationToken)
    {
        if (snapshot.DatabaseFailingSince is { } since && now - since >= _options.DatabaseDownAfter)
        {
            await Alerts.RaiseAsync(
                nameof(AlertKind.Database),
                "Database unreachable",
                $"""
                The database hasn't answered since {since:u} ({AlertBook.FormatDuration(now - since)}).
                Last problem: {snapshot.DatabaseError}
                Pages and the scheduled jobs fail until it's back. Check the SQL Server container.

                {context}
                """,
                cancellationToken);
        }
        else if (snapshot.DatabaseFailingSince is null && snapshot.DatabaseOkAt is not null)
        {
            await Alerts.ResolveAsync(nameof(AlertKind.Database), cancellationToken);
        }
    }

    private async Task CheckJobAsync(
        ScheduledJob job, AlertKind kind, AppHealthSnapshot snapshot, DateTimeOffset now, string context, CancellationToken cancellationToken)
    {
        var name = Name(job);
        var lastRun = snapshot.LastRuns.GetValueOrDefault(job);
        DateTimeOffset? lastSuccess = snapshot.LastSuccesses.TryGetValue(job, out var success) ? success : null;
        // Measured from startup too, so a restart gets a grace period rather than an instant alert.
        var sinceSuccess = now - (lastSuccess ?? snapshot.StartedAt);

        string? reason = null;
        if (lastRun is { Succeeded: false })
            reason = $"The run at {lastRun.CompletedAt:u} failed for {lastRun.Failures} of {lastRun.Users} users.\nLast error: {lastRun.LastError}";
        else if (sinceSuccess > _options.JobOverdueAfter)
            reason = $"No run has completed for {AlertBook.FormatDuration(sinceSuccess)} (it runs daily).";

        if (reason is not null)
        {
            await Alerts.RaiseAsync(
                kind.ToString(),
                $"{name} failing",
                $"""
                {reason}
                Details are in the log (journalctl).

                {context}
                """,
                cancellationToken);
        }
        else if (lastRun is { Succeeded: true })
        {
            await Alerts.ResolveAsync(kind.ToString(), cancellationToken);
        }
    }

    private static string Name(ScheduledJob job) => job switch
    {
        ScheduledJob.NightlyBackups => "Nightly backups",
        ScheduledJob.MorningReminders => "Morning reminders",
        _ => job.ToString(),
    };

    private static string Describe(AppHealthSnapshot snapshot, DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.AppendLine($"Host: {Environment.MachineName}");
        text.AppendLine($"Now: {now:u}");
        text.AppendLine($"Started: {snapshot.StartedAt:u}");
        text.AppendLine($"Database: {(snapshot.DatabaseFailingSince is { } since ? $"not answering since {since:u}" : snapshot.DatabaseOkAt is { } ok ? $"answered {ok:u}" : "not checked yet")}");
        foreach (var job in Enum.GetValues<ScheduledJob>())
        {
            text.AppendLine(snapshot.LastRuns.TryGetValue(job, out var run)
                ? $"{Name(job)}: last run {run.CompletedAt:u}, {run.Users} users, {run.Failures} failed"
                : $"{Name(job)}: no run since startup");
        }

        return text.ToString();
    }
}
