using System.Diagnostics;
using Coravel.Invocable;
using Microsoft.EntityFrameworkCore;
using GreenHedges.HomeServer.Common.Web.Auth;
using GreenHedges.HomeServer.VehicleManager.Data;
using GreenHedges.HomeServer.VehicleManager.Monitoring;

namespace GreenHedges.HomeServer.VehicleManager.Services;

/// <summary>
/// The nightly backup (scheduled in <c>Program.cs</c>): emails each user with a backup email address
/// a backup, if their data has changed since their last scheduled backup.
/// </summary>
public class ScheduledBackups(AppDbContext db, IServiceScopeFactory scopes, AppHealth health, ILogger<ScheduledBackups> logger) : IInvocable
{
    public async Task Invoke()
    {
        List<string> users;
        try
        {
            users = await UsersToBackUpAsync();
        }
        catch (Exception ex)
        {
            // The whole run failed (e.g. the database is down): record it for the alerts, and let Coravel log it.
            health.RecordRun(ScheduledJob.NightlyBackups, 0, 1, ex);
            throw;
        }

        await RunAsync(users);
    }

    /// <summary>
    /// Backs up each of <paramref name="users"/>, one failure not stopping the others, and records the run in
    /// <see cref="AppHealth"/>. Tests call this with their own users; <see cref="Invoke"/> covers everyone.
    /// </summary>
    public async Task RunAsync(IReadOnlyCollection<string> users)
    {
        // A trace per run with a span per user (Telemetry), so the database, email and lookup calls for each user sit under it.
        using var run = Telemetry.Source.StartActivity("Scheduled backups");
        run?.SetTag("vehiclemanager.users", users.Count);
        var failures = 0;
        Exception? lastError = null;
        foreach (var userId in users)
        {
            using var span = Telemetry.Source.StartActivity("Back up user");
            span?.SetTag("enduser.id", userId);
            try
            {
                var result = await BackUpAsync(userId);
                span?.SetTag("vehiclemanager.backup.result", result.ToString());
                logger.LogInformation("Scheduled backup for {UserId}: {Result}", userId, result);
            }
            // One user's failure (e.g. Resend refusing their address) shouldn't stop the others' backups.
            catch (Exception ex)
            {
                failures++;
                lastError = ex;
                span?.AddException(ex).SetStatus(ActivityStatusCode.Error);
                logger.LogError(ex, "Scheduled backup for {UserId} failed", userId);
            }
        }

        health.RecordRun(ScheduledJob.NightlyBackups, users.Count, failures, lastError);
    }

    /// <summary>Everyone with a backup email address. Deliberately across all users, so it bypasses the query filters.</summary>
    public Task<List<string>> UsersToBackUpAsync() =>
        db.UserSettings.IgnoreQueryFilters()
            .Where(s => s.BackupEmail != null && s.BackupEmail != "")
            .Select(s => s.UserId)
            .ToListAsync();

    /// <summary>Emails one user's backup if it has changed, acting as that user in a scope of its own.</summary>
    public async Task<BackupEmailResult> BackUpAsync(string userId)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUser>().ActAs(userId);
        return await scope.ServiceProvider.GetRequiredService<BackupEmailer>().EmailAsync(scheduled: true);
    }
}
