namespace GreenHedges.HomeServer.Collectors.Weather.Rtl433;

/// <summary>Why an rtl_433 run ended.</summary>
public enum RunEnd
{
    /// <summary>rtl_433 exited by itself.</summary>
    Exited,

    /// <summary>No line for <see cref="Rtl433Options.SilenceTimeout"/>: killed.</summary>
    Silent,

    /// <summary>The executable couldn't be started (e.g. not installed).</summary>
    StartFailed,

    /// <summary>The collector is stopping.</summary>
    Stopped,
}

/// <param name="ExitCode">rtl_433's exit code, if it ran and exited.</param>
/// <param name="DongleMissing">rtl_433 reported that it found no usable RTL-SDR device.</param>
/// <param name="Lines">Lines read from stdout in this run.</param>
public sealed record RunResult(RunEnd End, int? ExitCode, bool DongleMissing, long Lines, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string? Error = null)
{
    public TimeSpan Duration => EndedAt - StartedAt;
}

/// <summary>
/// What the receiver has done lately, for the alert checks and the heartbeat: whether rtl_433 is
/// running, when it last wrote a line, how often it has restarted, and whether the dongle is missing.
/// </summary>
/// <remarks>
/// Restarts count for an hour, except that a run that has lasted <see cref="Rtl433Options.StableAfter"/> and is writing
/// lines clears the ones before it: a short burst (a USB blip) shouldn't hold the receiver alert open for an hour after
/// rtl_433 has recovered. A crash loop's runs are shorter than that, so its restarts still add up.
/// </remarks>
public sealed class ReceiverHealth(TimeProvider time, Rtl433Options? options = null)
{
    private readonly TimeSpan _stableAfter = (options ?? new Rtl433Options()).StableAfter;

    private readonly Lock _lock = new();
    private readonly Queue<DateTimeOffset> _restarts = new();
    private bool _running;
    private DateTimeOffset? _runStartedAt;
    private DateTimeOffset? _lastLineAt;
    private RunResult? _lastRun;
    private DateTimeOffset? _dongleMissingSince;

    public DateTimeOffset StartedAt { get; } = time.GetUtcNow();

    internal void RecordRunStarted()
    {
        lock (_lock)
        {
            _running = true;
            _runStartedAt = time.GetUtcNow();
        }
    }

    internal void RecordLine()
    {
        lock (_lock)
        {
            _lastLineAt = time.GetUtcNow();
            // Lines mean the dongle is there, whatever happened before.
            _dongleMissingSince = null;
        }
    }

    internal void RecordRunEnded(RunResult result)
    {
        lock (_lock)
        {
            _running = false;
            _lastRun = result;
            if (result.End != RunEnd.Stopped)
            {
                _restarts.Enqueue(result.EndedAt);
            }

            if (result.DongleMissing)
            {
                _dongleMissingSince ??= result.EndedAt;
            }
        }
    }

    public ReceiverHealthSnapshot Snapshot()
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            var hourAgo = now - TimeSpan.FromHours(1);
            while (_restarts.TryPeek(out var oldest) && oldest < hourAgo)
            {
                _restarts.Dequeue();
            }

            // A stable run writing lines: the restarts before it are over. Cleared for good, so the next end
            // starts a fresh count rather than bringing the old ones back.
            if (_running
                && _runStartedAt is { } started
                && now - started >= _stableAfter
                && _lastLineAt is { } line
                && line >= started)
            {
                while (_restarts.TryPeek(out var oldest) && oldest <= started)
                {
                    _restarts.Dequeue();
                }
            }

            return new ReceiverHealthSnapshot(StartedAt, _running, _runStartedAt, _lastLineAt, _lastRun, _dongleMissingSince, _restarts.Count);
        }
    }
}

/// <param name="LastLineAt">When rtl_433 last wrote any line, from any device.</param>
/// <param name="DongleMissingSince">Set while rtl_433 keeps reporting no usable RTL-SDR device; cleared by the next line.</param>
/// <param name="EndsInLastHour">Runs that ended (crashed, exited or were killed) in the last hour, not counting those before a stable run.</param>
public sealed record ReceiverHealthSnapshot(
    DateTimeOffset StartedAt,
    bool IsRunning,
    DateTimeOffset? RunStartedAt,
    DateTimeOffset? LastLineAt,
    RunResult? LastRun,
    DateTimeOffset? DongleMissingSince,
    int EndsInLastHour);
