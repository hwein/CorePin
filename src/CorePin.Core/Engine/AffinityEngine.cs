using System.Diagnostics;
using System.Globalization;
using CorePin.Core.Diagnostics;
using CorePin.Core.Platform;
using CorePin.Core.Primitives;
using CorePin.Core.Rules;
using CorePin.Core.Time;

namespace CorePin.Core.Engine;

/// Single-threaded, timer-free. Every decision of the watcher lives here.
public sealed class AffinityEngine
{
    private readonly IProcessInventory _inventory;
    private readonly ILog _log;

    private readonly PinList _pins = new();
    private readonly ProcessPinner _pinner;
    private readonly RuleRelease _release;
    private readonly ReleaseFailures _releaseFailed = new();
    private readonly RuleTransitionLog _transitions;
    private readonly Dictionary<string, Rule> _byExe = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _liveRuleIds = [];

    private IReadOnlyList<ProcessEntry> _lastSnapshot = [];
    private long _tickNumber;

    public AffinityEngine(IProcessInventory inventory, IAffinityAccess access,
                          IClock clock, ILog log, AffinityMask machineMask)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);

        _inventory = inventory;
        _log = log;
        MachineMask = machineMask;
        _pinner = new ProcessPinner(access, log, _pins);
        _release = new RuleRelease(access, log, _pins);
        _transitions = new RuleTransitionLog(clock, log);
    }

    public int PinnedProcessCount => _pins.Count;

    /// EngineHost branches on RuleEvaluation.Check and needs the same mask the engine uses.
    internal AffinityMask MachineMask { get; }

    public IReadOnlyList<RuleStatus> Tick(RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        long started = Stopwatch.GetTimestamp();
        _tickNumber++;

        DropVanishedRules(rules);

        var snapshot = _inventory.ListOwnSession();
        _lastSnapshot = snapshot;

        var matches = MatchByName(rules, snapshot);

        var statuses = new List<RuleStatus>(rules.Rules.Count);
        foreach (var rule in rules.Rules)
            statuses.Add(Evaluate(rule, matches[rule.Id]));

        _pins.PruneTo(snapshot);

        for (int i = 0; i < statuses.Count; i++)
            _transitions.Observe(rules.Rules[i].ExeName, statuses[i]);

        if (_log.IsEnabled(LogLevel.Debug))
        {
            string elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds
                .ToString("0.00", CultureInfo.InvariantCulture);
            _log.Debug("engine", $"tick #{_tickNumber}: {rules.Rules.Count} rules checked, " +
                                 $"{TotalMatches(matches)} processes matched, {elapsed} ms");
        }

        return statuses;
    }

    public IReadOnlyList<RuleStatus> ApplyRule(RuleSet rules, Guid ruleId)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var rule = rules.ById(ruleId);
        if (rule is null)
        {
            _log.Debug("engine", $"apply skipped: rule {ruleId} is not in the rule set");
            return [];
        }

        if (_lastSnapshot.Count == 0) _lastSnapshot = _inventory.ListOwnSession();

        var status = Evaluate(rule, MatchByName(rules, _lastSnapshot)[ruleId]);
        _transitions.Observe(rule.ExeName, status);
        return [status];
    }

    public void Release(Guid ruleId, ReleaseReason reason)
    {
        var outcome = _release.Run(ruleId, reason);

        if (reason == ReleaseReason.Removed)
        {
            _transitions.Forget(ruleId);
            _releaseFailed.Forget(ruleId);
            return;
        }

        // A run over an empty pin list is no second release, so it must not clear what the first one remembered.
        if (outcome.VisitedAny) _releaseFailed.Replace(ruleId, outcome.Failed);
    }

    private RuleStatus Evaluate(Rule rule, List<ProcessEntry> matches)
    {
        var check = RuleEvaluation.Check(rule, MachineMask);

        int stillPinned = _releaseFailed.StillPinned(rule.Id, check, matches);
        if (stillPinned > 0) return RuleStatusBuilder.ReleaseFailed(rule.Id, matches, stillPinned);

        return check == RuleCheck.Apply
            ? RuleStatusBuilder.Pinned(rule.Id, _pinner.Run(rule, matches))
            : RuleStatusBuilder.Untouched(rule.Id, check, matches);
    }

    private void DropVanishedRules(RuleSet rules)
    {
        _liveRuleIds.Clear();
        foreach (var rule in rules.Rules) _liveRuleIds.Add(rule.Id);

        _transitions.RetainOnly(_liveRuleIds);
        _releaseFailed.RetainOnly(_liveRuleIds);
    }

    /// Every rule gets an entry — an empty list on no hit — and every list ascends by PID.
    private Dictionary<Guid, List<ProcessEntry>> MatchByName(RuleSet rules, IReadOnlyList<ProcessEntry> snapshot)
    {
        _byExe.Clear();
        var matches = new Dictionary<Guid, List<ProcessEntry>>(rules.Rules.Count);

        foreach (var rule in rules.Rules)
        {
            matches[rule.Id] = [];
            if (_byExe.TryAdd(rule.ExeName, rule)) continue;

            _log.Debug("engine", $"rule '{rule.ExeName}': duplicate exeName ignored, " +
                                 $"rule {_byExe[rule.ExeName].Id} already covers this program");
        }

        foreach (var process in snapshot)
            if (_byExe.TryGetValue(process.ExeName, out var owner)) matches[owner.Id].Add(process);

        foreach (var list in matches.Values)
            list.Sort((left, right) => left.Pid.CompareTo(right.Pid));

        return matches;
    }

    private static int TotalMatches(Dictionary<Guid, List<ProcessEntry>> matches)
    {
        int total = 0;
        foreach (var list in matches.Values) total += list.Count;
        return total;
    }
}
