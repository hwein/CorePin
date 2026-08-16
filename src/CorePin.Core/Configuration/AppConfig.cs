using CorePin.Core.Diagnostics;
using CorePin.Core.Rules;

namespace CorePin.Core.Configuration;

public sealed record MachineInfo(string CpuName, int LogicalProcessors);

public sealed record WindowBounds(int X, int Y, int W, int H);

public sealed record Settings
{
    public int PollIntervalMs { get; init; } = 1000;

    /// Read and written, but NOT evaluated in v0.1.
    public string StartWithWindows { get; init; } = "normal";

    public LogLevel LogLevel { get; init; } = LogLevel.Info;

    public WindowBounds? WindowBounds { get; init; }
}

public sealed record AppConfig
{
    public required int SchemaVersion { get; init; }
    public required MachineInfo Machine { get; init; }
    public required Settings Settings { get; init; }
    public required RuleSet Rules { get; init; }

    public static AppConfig Empty() => new()
    {
        SchemaVersion = 1,
        Machine = new MachineInfo("", 0),
        Settings = new Settings(),
        Rules = RuleSet.Empty,
    };
}

public enum ConfigLoadOutcome { Loaded, Missing, Unreadable, Corrupt, TooNew }

/// Config.Rules is ALWAYS RuleSet.Empty — the rules read from the file live in RawRules.
public sealed record ConfigLoadResult(
    AppConfig Config,
    RuleSet RawRules,
    ConfigLoadOutcome Outcome,
    string? Detail,
    int RulesSkipped);
