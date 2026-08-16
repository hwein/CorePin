using CorePin.Core.Diagnostics;
using CorePin.Core.Rules;
using CorePin.Core.Time;

namespace CorePin.Core.Engine;

internal readonly record struct LoggedRuleState(
    RuleState State, BlockReason Reason, int AffectedProcesses, int MatchedProcesses);

/// Writes one line per state change, not per state. Only the way into Idle is damped.
internal sealed class RuleTransitionLog(IClock clock, ILog log)
{
    private const int IdleHysteresisMs = 3000;

    private readonly Dictionary<Guid, LoggedRuleState> _lastLogged = [];
    private readonly Dictionary<Guid, long> _idleSinceMs = [];

    internal void Observe(string exeName, RuleStatus status)
    {
        var current = new LoggedRuleState(status.State, status.Reason,
                                          status.AffectedProcesses, status.MatchedProcesses);

        if (!_lastLogged.TryGetValue(status.RuleId, out var previous))
        {
            _lastLogged[status.RuleId] = current;
            if (current.State == RuleState.Idle) return;      // Idle is the silent ground state
            log.Information("engine", Format(null, current, exeName, status));
            return;
        }

        if (status.State == RuleState.Idle && previous.State != RuleState.Idle)
        {
            if (!_idleSinceMs.TryGetValue(status.RuleId, out long since))
            {
                _idleSinceMs[status.RuleId] = clock.MonotonicMs;
                return;
            }
            if (clock.MonotonicMs - since < IdleHysteresisMs) return;
        }
        else
        {
            _idleSinceMs.Remove(status.RuleId);
        }

        if (previous == current) return;

        _lastLogged[status.RuleId] = current;
        log.Information("engine", Format(previous, current, exeName, status));
    }

    internal void Forget(Guid ruleId)
    {
        _lastLogged.Remove(ruleId);
        _idleSinceMs.Remove(ruleId);
    }

    internal void RetainOnly(HashSet<Guid> ruleIds)
    {
        List<Guid>? gone = null;
        foreach (var ruleId in _lastLogged.Keys)
            if (!ruleIds.Contains(ruleId)) (gone ??= []).Add(ruleId);

        foreach (var ruleId in _idleSinceMs.Keys)
            if (!ruleIds.Contains(ruleId)) (gone ??= []).Add(ruleId);

        if (gone is null) return;

        foreach (var ruleId in gone) Forget(ruleId);
    }

    private static string Format(LoggedRuleState? previous, LoggedRuleState current,
                                 string exeName, RuleStatus status)
    {
        string counts = $"{status.AffectedProcesses} of {status.MatchedProcesses}";

        if (previous is { } p && p.State == current.State && p.Reason == current.Reason)
            return $"rule '{exeName}': {current.State} ({p.AffectedProcesses} of {p.MatchedProcesses} -> {counts})";

        return $"rule '{exeName}': {previous?.State.ToString() ?? "Idle"} -> {current.State} ({Detail(status)})";
    }

    private static string Detail(RuleStatus status)
    {
        string counts = $"{status.AffectedProcesses} of {status.MatchedProcesses}";
        if (status.Reason != BlockReason.None) return $"{counts}, {status.Reason}";
        return status.FirstPid == 0 ? counts : $"PID {status.FirstPid}, {counts}";
    }
}
