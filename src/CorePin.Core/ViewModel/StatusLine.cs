namespace CorePin.Core.ViewModel;

/// The four forms of the status line and the staged "ago" format behind the third one.
public static class StatusLine
{
    public const string NoRules = "Watching for apps — no rules yet";

    public const string Stopped = "Stopped watching — quit from the tray icon and start CorePin again";

    public const string DebugTopologyHint = "Debug topology active — changes won't be saved.";

    public static string Compose(int ruleCount, string? appliedExe, TimeSpan? sinceApplied)
    {
        if (ruleCount == 0) return NoRules;
        if (appliedExe is null || sinceApplied is not { } elapsed) return $"Watching {ruleCount} rules";

        return $"Watching {ruleCount} rules · applied {appliedExe} {Ago(elapsed)}";
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
