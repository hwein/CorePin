using System.Globalization;
using CorePin.Core.Diagnostics;

namespace CorePin.App;

/// Parse never logs — it runs before the logger exists.
public sealed record StartupOptions
{
    public bool Tray { get; init; }

    public bool DumpTopology { get; init; }

    public required IReadOnlyList<string> Unknown { get; init; }

    /// Deliberately without #if DEBUG: a user-facing diagnostic aid, not a developer tool.
    public LogLevel? LogLevelOverride { get; init; }

    public string? InvalidLogLevelValue { get; init; }      // given, but with an unreadable value

#if DEBUG
    public string? DebugTopologyFile { get; init; }
    public int? DebugGroupCount { get; init; }
    public bool DebugDumpRaw { get; init; }
#endif

    public static StartupOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var unknown = new List<string>();
        bool tray = false;
        bool dump = false;
        LogLevel? level = null;
        string? invalid = null;
#if DEBUG
        string? topologyFile = null;
        int? groupCount = null;
        bool dumpRaw = false;
#endif

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (Is(arg, "--tray")) { tray = true; continue; }
            if (Is(arg, "--dump-topology")) { dump = true; continue; }

            if (Is(arg, "--log-level"))
            {
                // The last occurrence wins entirely; a stale sibling state would log a lie.
                string? value = Value(args, ref i);
                if (LogLevelNames.TryParse(value, out var parsed)) { level = parsed; invalid = null; }
                else { invalid = value ?? string.Empty; level = null; }
                continue;
            }

#if DEBUG
            if (Is(arg, "--debug-topology")) { topologyFile = Value(args, ref i); continue; }
            if (Is(arg, "--debug-dump-raw")) { dumpRaw = true; continue; }
            if (Is(arg, "--debug-groups"))
            {
                string? value = Value(args, ref i);
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    groupCount = n;
                else
                    unknown.Add(arg);
                continue;
            }
#endif

            unknown.Add(arg);
        }

        return new StartupOptions
        {
            Tray = tray,
            DumpTopology = dump,
            Unknown = unknown,
            LogLevelOverride = level,
            InvalidLogLevelValue = invalid,
#if DEBUG
            DebugTopologyFile = topologyFile,
            DebugGroupCount = groupCount,
            DebugDumpRaw = dumpRaw,
#endif
        };
    }

    private static bool Is(string arg, string name)
        => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);

    private static string? Value(string[] args, ref int i)
        => i + 1 < args.Length ? args[++i] : null;
}
