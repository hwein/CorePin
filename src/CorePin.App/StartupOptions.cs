using CorePin.Core.Diagnostics;

namespace CorePin.App;

/// Phase 0 excerpt of S01 §3.9/§6.3. Parse never logs — it runs before the logger exists.
///
/// --tray and --dump-topology are NOT parsed yet: without S08 and S04 they would have no
/// effect, and a parsed option without effect is a silent failure (P4). They therefore end
/// up in Unknown and produce the documented app.unknown-argument warning.
public sealed record StartupOptions
{
    public required IReadOnlyList<string> Unknown { get; init; }

    /// --log-level. Deliberately without #if DEBUG: a diagnostic aid for the user, not a
    /// developer tool — it must exist in the release build (S01 §6.3, C-3).
    public LogLevel? LogLevelOverride { get; init; }        // null = argument not given

    public string? InvalidLogLevelValue { get; init; }      // given, but with an unreadable value

    public static StartupOptions Parse(string[] args)
    {
        var unknown = new List<string>();
        LogLevel? level = null;
        string? invalid = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--log-level", StringComparison.OrdinalIgnoreCase))
            {
                string? value = i + 1 < args.Length ? args[++i] : null;
                if (TryParseLevel(value, out var parsed)) level = parsed;
                else invalid = value ?? string.Empty;
                continue;
            }

            unknown.Add(arg);
        }

        return new StartupOptions
        {
            Unknown = unknown,
            LogLevelOverride = level,
            InvalidLogLevelValue = invalid,
        };
    }

    private static bool TryParseLevel(string? value, out LogLevel level)
    {
        level = LogLevel.Info;
        if (value is null) return false;

        if (string.Equals(value, "debug", StringComparison.OrdinalIgnoreCase)) level = LogLevel.Debug;
        else if (string.Equals(value, "info", StringComparison.OrdinalIgnoreCase)) level = LogLevel.Info;
        else if (string.Equals(value, "warn", StringComparison.OrdinalIgnoreCase)) level = LogLevel.Warn;
        else return false;

        return true;
    }
}
