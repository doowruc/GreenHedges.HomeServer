using System.Text;
using Microsoft.Extensions.Options;
using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Common.Web.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Monitoring;

/// <summary>
/// Pay & Pensions' checks, every <c>Alerts:CheckInterval</c>: the database answering, and (through
/// <see cref="BackupMonitor"/>) the nightly database backups and their off-site copies. As Vehicle Manager's, without its
/// scheduled jobs. Emails and their persistence are the shared <see cref="AlertBook"/>'s.
/// </summary>
/// <remarks>
/// <see cref="AppHealth"/> starts empty after a restart while open alerts are restored, so the database alert resolves
/// only once it has answered since startup.
/// </remarks>
public sealed class PayAndPensionsAlertMonitor(
    AppHealth health,
    IDatabaseProbe database,
    BackupMonitor backups,
    AlertBook alerts,
    IOptions<AlertOptions> alertOptions,
    IOptions<PayAndPensionsAlertOptions> options,
    TimeProvider time,
    ILogger<PayAndPensionsAlertMonitor> logger) : AlertMonitorService(alerts, alertOptions, time, logger)
{
    public const string DatabaseKey = "Database";

    private static readonly string[] Keys = [DatabaseKey, .. BackupMonitor.Keys];

    private readonly PayAndPensionsAlertOptions _options = options.Value;

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

        if (snapshot.DatabaseFailingSince is { } since && now - since >= _options.DatabaseDownAfter)
        {
            await Alerts.RaiseAsync(
                DatabaseKey,
                "Database unreachable",
                $"""
                The database hasn't answered since {since:u} ({AlertBook.FormatDuration(now - since)}).
                Last problem: {snapshot.DatabaseError}
                Pages and the nightly backup fail until it's back. Check the SQL Server container.

                {context}
                """,
                cancellationToken);
        }
        else if (snapshot.DatabaseFailingSince is null && snapshot.DatabaseOkAt is not null)
        {
            await Alerts.ResolveAsync(DatabaseKey, cancellationToken);
        }

        await backups.CheckAsync(Alerts, now, context, cancellationToken);
    }

    private static string Describe(AppHealthSnapshot snapshot, DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.AppendLine($"Host: {Environment.MachineName}");
        text.AppendLine($"Now: {now:u}");
        text.AppendLine($"Started: {snapshot.StartedAt:u}");
        text.AppendLine($"Database: {(snapshot.DatabaseFailingSince is { } since ? $"not answering since {since:u}" : snapshot.DatabaseOkAt is { } ok ? $"answered {ok:u}" : "not checked yet")}");
        return text.ToString();
    }
}
