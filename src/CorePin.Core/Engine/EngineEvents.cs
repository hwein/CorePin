using CorePin.Core.Rules;

namespace CorePin.Core.Engine;

public enum RuleChangeKind { None, Added, SelectionChanged, EnabledChanged, Removed }

public readonly record struct RuleChange(RuleChangeKind Kind, Guid RuleId);

public enum ReleaseReason { Disabled, Removed, AllThreadsSelected }

public enum BlockReason { None, NeedsAdminRights, BlockedByWindows }

public readonly record struct RuleStatus(
    Guid RuleId,
    RuleState State,
    // Name-reconfirmed hits where a process loop ran, raw enumeration hits everywhere else.
    int MatchedProcesses,
    int AffectedProcesses,
    int FirstPid,
    BlockReason Reason);

public readonly record struct EngineHeartbeat(
    DateTime TickUtc,
    int RulesWatched,
    string? LastAppliedExe,
    DateTime? LastAppliedUtc);

public readonly record struct EngineFault(string Message, int ConsecutiveFailures);
