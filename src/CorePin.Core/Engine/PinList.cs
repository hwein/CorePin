using CorePin.Core.Platform;

namespace CorePin.Core.Engine;

internal readonly record struct PinEntry(Guid RuleId, string ExeName, DateTime StartUtc);

/// PID → entry. The only state the watcher keeps across ticks; it never leaves the engine.
internal sealed class PinList
{
    private readonly Dictionary<int, PinEntry> _entries = [];
    private readonly HashSet<int> _alive = [];

    internal int Count => _entries.Count;

    internal void Set(int pid, PinEntry entry) => _entries[pid] = entry;

    internal void Remove(int pid) => _entries.Remove(pid);

    internal List<(int Pid, PinEntry Entry)> Of(Guid ruleId)
    {
        var result = new List<(int Pid, PinEntry Entry)>();
        foreach (var (pid, entry) in _entries)
            if (entry.RuleId == ruleId) result.Add((pid, entry));

        result.Sort((left, right) => left.Pid.CompareTo(right.Pid));
        return result;
    }

    internal void PruneTo(IReadOnlyList<ProcessEntry> snapshot)
    {
        if (_entries.Count == 0) return;

        _alive.Clear();
        foreach (var process in snapshot) _alive.Add(process.Pid);

        List<int>? gone = null;
        foreach (int pid in _entries.Keys)
            if (!_alive.Contains(pid)) (gone ??= []).Add(pid);

        if (gone is null) return;

        foreach (int pid in gone) _entries.Remove(pid);
    }
}
