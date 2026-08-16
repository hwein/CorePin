using CorePin.Core.Platform;
using CorePin.Core.Rules;

namespace CorePin.Core.Engine;

/// Rule → the PIDs whose release failed. Only ever affects the reported rule state.
internal sealed class ReleaseFailures
{
    private readonly Dictionary<Guid, HashSet<int>> _byRule = [];

    /// Replacing, never a zero: an empty set drops the key instead of being stored.
    internal void Replace(Guid ruleId, IReadOnlyCollection<int> pids)
    {
        if (pids.Count == 0) _byRule.Remove(ruleId);
        else _byRule[ruleId] = [.. pids];
    }

    internal void Forget(Guid ruleId) => _byRule.Remove(ruleId);

    internal void RetainOnly(HashSet<Guid> ruleIds)
    {
        List<Guid>? gone = null;
        foreach (var ruleId in _byRule.Keys)
            if (!ruleIds.Contains(ruleId)) (gone ??= []).Add(ruleId);

        if (gone is null) return;

        foreach (var ruleId in gone) _byRule.Remove(ruleId);
    }

    /// Applies the two match-picture deletion conditions and counts what is left.
    internal int StillPinned(Guid ruleId, RuleCheck check, IReadOnlyList<ProcessEntry> matches)
    {
        if (!_byRule.TryGetValue(ruleId, out var pids)) return 0;

        if (check == RuleCheck.Apply)
        {
            _byRule.Remove(ruleId);
            return 0;
        }

        int running = 0;
        foreach (var process in matches)
            if (pids.Contains(process.Pid)) running++;

        if (running == 0) _byRule.Remove(ruleId);
        return running;
    }
}
