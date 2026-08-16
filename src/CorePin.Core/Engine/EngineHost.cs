using System.Collections.Concurrent;
using CorePin.Core.Diagnostics;
using CorePin.Core.Rules;
using CorePin.Core.Time;

namespace CorePin.Core.Engine;

/// One background thread, one queue, no timer: tick and debounce deadlines come out of the
/// loop's own wait.
public sealed class EngineHost : IDisposable
{
    private const int DebounceMs = 200;
    private const int MaxConsecutiveFailures = 10;
    private const int StopTimeoutMs = 2000;

    private readonly AffinityEngine _engine;
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly int _pollIntervalMs;

    private readonly ConcurrentQueue<EngineCommand> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Dictionary<Guid, long> _dueAt = [];
    private readonly Dictionary<Guid, RuleState> _lastState = [];

    private Thread? _thread;
    private bool _gaveUp;
    private RuleSet _rules = RuleSet.Empty;
    private string? _lastAppliedExe;
    private DateTime? _lastAppliedUtc;

    public EngineHost(AffinityEngine engine, IClock clock, ILog log, int pollIntervalMs)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);

        _engine = engine;
        _clock = clock;
        _log = log;
        _pollIntervalMs = pollIntervalMs;
    }

    /// Raised on the worker thread.
    public event Action<IReadOnlyList<RuleStatus>>? StatusChanged;

    /// Raised on the worker thread.
    public event Action<EngineHeartbeat>? Heartbeat;

    /// Raised once when the loop gives up; no further event follows.
    public event Action<EngineFault>? Faulted;

    public void Start()
    {
        if (_thread is not null) return;

        _thread = new Thread(Loop) { Name = "CorePin.Engine", IsBackground = true };
        _log.Information("engine", $"watcher started, poll interval {_pollIntervalMs} ms");
        _thread.Start();
    }

    public void Stop()
    {
        var thread = _thread;
        if (thread is null) return;

        _queue.Enqueue(EngineCommand.Stop);
        _signal.Set();

        if (!thread.Join(StopTimeoutMs))
        {
            _log.Warning("engine", $"worker thread did not stop within {StopTimeoutMs} ms, continuing shutdown");
            return;
        }

        _thread = null;
        if (!_gaveUp) _log.Information("engine", "watcher stopped");
    }

    public void Submit(RuleSet rules, RuleChange change)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _queue.Enqueue(new EngineCommand(rules, change, IsStop: false));
        _signal.Set();
    }

    public void Dispose()
    {
        Stop();

        // A worker that outlived the stop deadline still waits on the signal; freeing it would kill that thread.
        if (_thread is null) _signal.Dispose();
    }

    private void Loop()
    {
        long nextTick = _clock.MonotonicMs;      // the first tick runs immediately
        int failures = 0;

        while (true)
        {
            long now = _clock.MonotonicMs;
            long due = Math.Min(nextTick, NextDebounceDue());
            int wait = (int)Math.Clamp(due - now, 0L, _pollIntervalMs);

            if (wait > 0) _signal.WaitOne(wait);

            try
            {
                while (_queue.TryDequeue(out var command))
                {
                    if (command.IsStop) return;
                    Handle(command);
                }

                now = _clock.MonotonicMs;
                if (now >= NextDebounceDue()) RunDueDebounced(now);
                if (now >= nextTick)
                {
                    RunTick();
                    nextTick = now + _pollIntervalMs;
                }

                failures = 0;
            }
            catch (Exception ex)
            {
                failures++;
                nextTick = _clock.MonotonicMs + _pollIntervalMs;   // one failure per poll interval, not a retry storm
                _log.Warning("engine", $"pass failed ({failures}): {ex}");
                if (failures < MaxConsecutiveFailures) continue;

                _gaveUp = true;
                _log.Critical("engine", $"watcher gave up after {failures} consecutive failures, monitoring stopped");
                Faulted?.Invoke(new EngineFault(ex.Message, failures));
                return;
            }
        }
    }

    private void Handle(EngineCommand command)
    {
        _rules = command.Rules;
        var change = command.Change;
        if (change.Kind == RuleChangeKind.None) return;

        _dueAt.Remove(change.RuleId);

        if (change.Kind == RuleChangeKind.Removed)
        {
            _engine.Release(change.RuleId, ReleaseReason.Removed);
            _lastState.Remove(change.RuleId);
            return;
        }

        var rule = _rules.ById(change.RuleId);
        if (rule is null) return;

        switch (RuleEvaluation.Check(rule, _engine.MachineMask))
        {
            case RuleCheck.Apply:
                _dueAt[change.RuleId] = _clock.MonotonicMs + DebounceMs;
                return;
            case RuleCheck.NoRestriction:
                _engine.Release(change.RuleId, ReleaseReason.AllThreadsSelected);
                break;
            case RuleCheck.Disabled:
                _engine.Release(change.RuleId, ReleaseReason.Disabled);
                break;
            default:
                break;                                  // Invalid touches no process
        }

        Publish(_engine.ApplyRule(_rules, change.RuleId));
    }

    private long NextDebounceDue()
    {
        long next = long.MaxValue;
        foreach (long at in _dueAt.Values)
            if (at < next) next = at;
        return next;
    }

    private void RunDueDebounced(long now)
    {
        List<Guid>? due = null;
        foreach (var (ruleId, at) in _dueAt)
            if (at <= now) (due ??= []).Add(ruleId);

        if (due is null) return;

        foreach (var ruleId in due)
        {
            _dueAt.Remove(ruleId);
            Publish(_engine.ApplyRule(_rules, ruleId));
        }
    }

    private void RunTick()
    {
        Publish(_engine.Tick(_rules));
        Heartbeat?.Invoke(new EngineHeartbeat(
            _clock.UtcNow, _rules.Rules.Count, _lastAppliedExe, _lastAppliedUtc));
    }

    private void Publish(IReadOnlyList<RuleStatus> statuses)
    {
        if (statuses.Count == 0) return;

        foreach (var status in statuses)
        {
            bool wasApplied = _lastState.TryGetValue(status.RuleId, out var previous)
                              && previous == RuleState.Applied;

            if (status.State == RuleState.Applied && !wasApplied)
            {
                _lastAppliedExe = _rules.ById(status.RuleId)?.ExeName;
                _lastAppliedUtc = _clock.UtcNow;
            }

            _lastState[status.RuleId] = status.State;
        }

        StatusChanged?.Invoke(statuses);
    }

    private readonly record struct EngineCommand(RuleSet Rules, RuleChange Change, bool IsStop)
    {
        internal static EngineCommand Stop => new(RuleSet.Empty, default, IsStop: true);
    }
}
