namespace CorePin.Core.ViewModel;

/// The four forms of the status line, the saved suffix and the staged "ago" format.
public static class StatusLine
{
    public const string NoRules = "Watching for apps — no rules yet";

    public const string Stopped = "Stopped watching — quit from the tray icon and start CorePin again";

    /// Appended to a watching line, never to Stopped: nothing is saved once watching ended.
    public const string SavedSuffix = " · saved";

    public const string DebugTopologyHint = "Debug topology active — changes won't be saved.";

    public static string Compose(int ruleCount, string? appliedExe, TimeSpan? sinceApplied)
    {
        if (ruleCount == 0) return NoRules;

        string noun = ruleCount == 1 ? "rule" : "rules";
        if (appliedExe is null || sinceApplied is not { } elapsed) return $"Watching {ruleCount} {noun}";

        return $"Watching {ruleCount} {noun} · applied {appliedExe} {Ago(elapsed)}";
    }

    /// Seconds, then minutes, then hours — nothing finer, or the line would reformat every second.
    public static string Ago(TimeSpan elapsed)
    {
        double seconds = Math.Max(0d, elapsed.TotalSeconds);
        if (seconds < 60d) return $"{(int)seconds} s ago";
        if (seconds < 3600d) return $"{(int)(seconds / 60d)} min ago";
        return $"{(int)(seconds / 3600d)} h ago";
    }
}
