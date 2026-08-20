namespace CorePin.Core.Rules;

/// One running program as the picker lists it — no path, the inventory has none.
public readonly record struct ProcessRow(int Pid, string ExeName);

public static class ProcessRowBuilder
{
    /// Deduplicates by ExeName (OrdinalIgnoreCase, lowest Pid wins), sorted alphabetically.
    public static IReadOnlyList<ProcessRow> Build(IReadOnlyList<ProcessRow> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);

        var lowest = new Dictionary<string, ProcessRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in processes)
        {
            if (lowest.TryGetValue(process.ExeName, out var kept) && kept.Pid <= process.Pid) continue;
            lowest[process.ExeName] = process;
        }

        return [.. lowest.Values.OrderBy(p => p.ExeName, StringComparer.OrdinalIgnoreCase)];
    }
}
