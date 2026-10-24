using System.Text;
using GreenHedges.HomeServer.Common.Alerts;
using GreenHedges.HomeServer.Common.Backups;
using GreenHedges.HomeServer.Collectors.Weather.History;
using GreenHedges.HomeServer.Collectors.Weather.Rtl433;
using GreenHedges.HomeServer.Collectors.Weather.Storage;
using Microsoft.Extensions.Options;

namespace GreenHedges.HomeServer.Collectors.Weather.Worker.Alerts;

/// <summary>
/// The Weather collector's checks: the station going quiet, the receiver (dongle, rtl_433), the station's
/// id changing, its battery, publishing the minute summaries, Weather Underground's history, and backups. Emails and their persistence are the shared <see cref="AlertBook"/>'s.
/// </summary>
public sealed class WeatherAlertMonitor(
    CollectorHealth health,
    ReceiverHealth receiver,
    WeatherArchive archive,
    AlertBook alerts,
    IOptions<AlertOptions> alertOptions,
    IOptions<WeatherAlertOptions> options,
    IOptions<StationOptions> stationOptions,
    IConfiguration configuration,
    IOptions<WundergroundOptions> wundergroundOptions,
    BackupMonitor backups,
    TimeProvider time,
    ILogger<WeatherAlertMonitor> logger) : AlertMonitorService(alerts, alertOptions, time, logger)
{
    private static readonly string[] Keys = [.. Enum.GetNames<AlertKind>(), .. BackupMonitor.Keys];

    private readonly WeatherAlertOptions _options = options.Value;
    private readonly StationOptions _station = stationOptions.Value;

    private enum AlertKind
    {
        /// <summary>Nothing from the station's ids for <see cref="WeatherAlertOptions.StationSilentAfter"/>.</summary>
        NoStationMessages,

        /// <summary>The dongle is missing, or rtl_433 runs keep ending (<see cref="WeatherAlertOptions.ReceiverEndsPerHour"/>).</summary>
        Receiver,

        /// <summary>A new id of the station's model is heard while the configured ids are silent.</summary>
        StationIdChanged,

        /// <summary><c>battery_ok</c> 0 for <see cref="WeatherAlertOptions.BatteryLowAfter"/>.</summary>
        BatteryLow,

        /// <summary>Publishing minute summaries failing for longer than <see cref="WeatherAlertOptions.PublishStalledAfter"/>.</summary>
        PublishStalled,

        /// <summary>Weather Underground history runs failing for longer than <see cref="WeatherAlertOptions.WundergroundFailingAfter"/>.</summary>
        WundergroundFailing,
    }

    protected override IReadOnlyCollection<string> AlertKeys => Keys;

    public override async Task CheckAsync(CancellationToken cancellationToken)
    {
        var snapshot = health.Snapshot();
        var radio = receiver.Snapshot();
        var now = Time.GetUtcNow();
        var context = Describe(snapshot, radio, now);

        await CheckReceiverAsync(radio, now, context, cancellationToken);

        // Without configured ids nothing counts as the station's (the collector logs a warning), and the
        // heartbeat stays silent, so the station checks have nothing to go on.
        if (_station.DeviceIds.Length > 0)
        {
            await CheckStationAsync(snapshot, now, context, cancellationToken);
            await CheckBatteryAsync(snapshot, now, context, cancellationToken);
        }

        await CheckPublishingAsync(snapshot, now, context, cancellationToken);
        await CheckWundergroundAsync(snapshot, now, context, cancellationToken);
        await backups.CheckAsync(Alerts, now, context, cancellationToken);
    }

    private async Task CheckWundergroundAsync(CollectorHealthSnapshot snapshot, DateTimeOffset now, string context, CancellationToken cancellationToken)
    {
        if (snapshot.WundergroundFailingSince is { } failingSince && now - failingSince > _options.WundergroundFailingAfter)
        {
            await RaiseAsync(
                AlertKind.WundergroundFailing,
                "Weather Underground history failing",
                $"""
                Fetching the station's history from Weather Underground has been failing for {FormatDuration(now - failingSince)} (retried hourly).
                The radio archive is unaffected; missing days are fetched once it works again.
                Last error: {(snapshot.LastWundergroundError is { } e ? $"{e.GetType().Name}: {e.Message}" : "none recorded")}

                {context}
                """,
                cancellationToken);
        }
        else if (!wundergroundOptions.Value.Enabled
                 || (snapshot.WundergroundFailingSince is null && snapshot.LastWundergroundSuccessAt is not null))
        {
            await ResolveAsync(AlertKind.WundergroundFailing, cancellationToken);
        }
    }

    private async Task CheckPublishingAsync(CollectorHealthSnapshot snapshot, DateTimeOffset now, string context, CancellationToken cancellationToken)
    {
        if (snapshot.PublishFailingSince is { } failingSince && now - failingSince > _options.PublishStalledAfter)
        {
            await RaiseAsync(
                AlertKind.PublishStalled,
                "Summaries for Grafana stalled",
                $"""
                Publishing minute summaries to SQL Server has been failing for {FormatDuration(now - failingSince)}.
                Messages are still being collected and archived; the summaries catch up once SQL Server is reachable.
                Last error: {(snapshot.LastPublishError is { } e ? $"{e.GetType().Name}: {e.Message}" : "none recorded")}

                {context}
                """,
                cancellationToken);
        }
        else if (!Summaries.SummaryPublisherService.IsEnabled(configuration)
                 || (snapshot.PublishFailingSince is null && snapshot.LastPublishSuccessAt is not null))
        {
            await ResolveAsync(AlertKind.PublishStalled, cancellationToken);
        }
    }

    private async Task CheckReceiverAsync(ReceiverHealthSnapshot radio, DateTimeOffset now, string context, CancellationToken cancellationToken)
    {
        var restarting = radio.EndsInLastHour >= _options.ReceiverEndsPerHour;
        if (radio.DongleMissingSince is { } missingSince)
        {
            await RaiseAsync(
                AlertKind.Receiver,
                "Receiver problem",
                $"""
                rtl_433 has found no usable RTL-SDR dongle since {missingSince:u} ({FormatDuration(now - missingSince)}).
                Check it's plugged in (lsusb | grep 2838) and that the DVB-T driver hasn't claimed it. rtl_433 is retried with a back-off.

                {context}
                """,
                cancellationToken);
        }
        else if (restarting)
        {
            await RaiseAsync(
                AlertKind.Receiver,
                "Receiver problem",
                $"""
                rtl_433 has stopped and been restarted {radio.EndsInLastHour} times in the last hour.

                {context}
                """,
                cancellationToken);
        }
        else if (radio.LastLineAt is { } line && now - line <= _options.StationSilentAfter)
        {
            // rtl_433 is writing lines now, not just "no problem seen yet".
            await ResolveAsync(AlertKind.Receiver, cancellationToken);
        }
    }

    private async Task CheckStationAsync(CollectorHealthSnapshot snapshot, DateTimeOffset now, string context, CancellationToken cancellationToken)
    {
        // Measured from startup too, so a restart gets a grace period rather than an instant alert.
        var last = snapshot.LastStationMessageAt ?? snapshot.StartedAt;
        var silentFor = now - last;

        if (silentFor <= _options.StationSilentAfter)
        {
            if (snapshot.LastStationMessageAt is not null)
            {
                // A real recent message from a configured id: the station is back, under an id we know.
                await ResolveAsync(AlertKind.NoStationMessages, cancellationToken);
                await ResolveAsync(AlertKind.StationIdChanged, cancellationToken);
            }

            return;
        }

        if (await FindNewStationIdsAsync(now, cancellationToken) is { Count: > 0 } newIds)
        {
            await RaiseAsync(
                AlertKind.StationIdChanged,
                "Station id changed",
                $"""
                Nothing from the station's id ({string.Join(", ", _station.DeviceIds)}) for {FormatDuration(silentFor)}, but a new {_station.Model} is being heard:

                {string.Join(Environment.NewLine, newIds.Select(d => $"  id {d.DeviceId}: first heard {d.FirstSeenUtc:u}, last {d.LastSeenUtc:u}, {d.MessageCount:N0} messages"))}

                After a battery change the sensor may pick a new id. If it's yours, add it to /etc/weathercollector/env as
                Station__DeviceIds__{_station.DeviceIds.Length}=<id> (keep the old one) and restart the collector.
                Its messages are archived meanwhile, but not yet as the station's.

                {context}
                """,
                cancellationToken);
        }

        // The id-change or receiver alert already explains the silence.
        if (!Alerts.IsOpen(nameof(AlertKind.StationIdChanged)) && !Alerts.IsOpen(nameof(AlertKind.Receiver)))
        {
            await RaiseAsync(
                AlertKind.NoStationMessages,
                "No station messages",
                $"""
                Nothing from the station for {FormatDuration(silentFor)} (it sends every 12 s).

                {context}
                """,
                cancellationToken);
        }
    }

    /// <summary>
    /// Devices of the station's model, not configured, heard recently and first heard after the configured
    /// ids were last heard: what a battery change that picked a new id looks like. A neighbour's sensor heard
    /// all along doesn't count.
    /// </summary>
    private async Task<IReadOnlyList<DeviceSummary>> FindNewStationIdsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        IReadOnlyList<DeviceSummary> devices;
        try
        {
            devices = await archive.GetDevicesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "Couldn't read the devices heard, to check for a new station id");
            return [];
        }

        var sameModel = devices.Where(d => d.Model == _station.Model).ToList();
        var stationLastSeen = sameModel
            .Where(d => _station.DeviceIds.Contains(d.DeviceId))
            .Select(d => (DateTimeOffset?)d.LastSeenUtc)
            .Max();

        return sameModel
            .Where(d => !_station.DeviceIds.Contains(d.DeviceId)
                && now - d.LastSeenUtc <= _options.StationSilentAfter
                && (stationLastSeen is null || d.FirstSeenUtc > stationLastSeen))
            .ToList();
    }

    private async Task CheckBatteryAsync(CollectorHealthSnapshot snapshot, DateTimeOffset now, string context, CancellationToken cancellationToken)
    {
        if (snapshot.BatteryLowSince is { } lowSince && now - lowSince >= _options.BatteryLowAfter)
        {
            await RaiseAsync(
                AlertKind.BatteryLow,
                "Sensor battery low",
                $"""
                The outdoor sensor has reported battery_ok = 0 since {lowSince:u} ({FormatDuration(now - lowSince)}).
                Change its batteries. Afterwards it may be heard under a new id (a "Station id changed" alert says which).

                {context}
                """,
                cancellationToken);
        }
        else if (snapshot.LastBatteryOk == 1)
        {
            await ResolveAsync(AlertKind.BatteryLow, cancellationToken);
        }
    }

    private Task RaiseAsync(AlertKind kind, string title, string body, CancellationToken cancellationToken) =>
        Alerts.RaiseAsync(kind.ToString(), title, body, cancellationToken);

    private Task ResolveAsync(AlertKind kind, CancellationToken cancellationToken) =>
        Alerts.ResolveAsync(kind.ToString(), cancellationToken);

    private static string Describe(CollectorHealthSnapshot snapshot, ReceiverHealthSnapshot radio, DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.AppendLine($"Host: {Environment.MachineName}");
        text.AppendLine($"Now: {now:u}");
        text.AppendLine($"Collector started: {snapshot.StartedAt:u}");
        text.AppendLine($"Last station message: {(snapshot.LastStationMessageAt is { } m ? $"{m:u} (id {snapshot.LastStationDeviceId})" : "none since startup")}");
        text.AppendLine($"Station battery: {snapshot.LastBatteryOk switch { 1 => "OK", 0 => "LOW", _ => "not reported since startup" }}");
        text.AppendLine($"rtl_433: {(radio.IsRunning ? $"running since {radio.RunStartedAt:u}" : "not running")}; last line {(radio.LastLineAt is { } l ? $"{l:u}" : "none since startup")}; {radio.EndsInLastHour} restarts in the last hour");
        if (radio.LastRun is { } run)
        {
            text.AppendLine($"Last rtl_433 run: ended {run.EndedAt:u} ({run.End}{(run.ExitCode is { } code ? $", exit code {code}" : "")}{(run.Error is { } error ? $": {error}" : "")}), {run.Lines:N0} lines");
        }

        if (snapshot.LastArchiveError is { } archiveError)
        {
            text.AppendLine($"Last archive error ({snapshot.LastArchiveErrorAt:u}): {archiveError.GetType().Name}: {archiveError.Message}");
        }

        return text.ToString();
    }

    private static string FormatDuration(TimeSpan duration) => AlertBook.FormatDuration(duration);
}
