using System.Diagnostics;

namespace CorePin.Core.Diagnostics;

/// The one vocabulary for level names in config.json, --log-level and written files.
public static class LogLevelNames
{
    public static bool TryParse(string? value, out LogLevel level)
    {
        level = LogLevel.Information;
        if (value is null) return false;

        if (Is(value, "trace")) level = LogLevel.Trace;
        else if (Is(value, "debug")) level = LogLevel.Debug;
        else if (Is(value, "information") || Is(value, "info")) level = LogLevel.Information;
        else if (Is(value, "warning") || Is(value, "warn")) level = LogLevel.Warning;
        else if (Is(value, "error")) level = LogLevel.Error;
        else if (Is(value, "critical")) level = LogLevel.Critical;
        else return false;

        return true;
    }

    public static string Format(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        _ => throw new UnreachableException(),
    };

    private static bool Is(string value, string name)
        => string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
}
