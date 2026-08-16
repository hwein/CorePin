using CorePin.Core.Platform;
using CorePin.Core.Rules;

namespace CorePin.Core.Engine;

/// The six-row precedence table. Row 0 is a separate entry point because it outranks Check.
internal static class RuleStatusBuilder
{
    internal static RuleStatus ReleaseFailed(Guid ruleId, IReadOnlyList<ProcessEntry> matches, int stillPinned)
        => new(ruleId, RuleState.Blocked, matches.Count, matches.Count - stillPinned,
               FirstPid(matches), BlockReason.BlockedByWindows);

    internal static RuleStatus Untouched(Guid ruleId, RuleCheck check, IReadOnlyList<ProcessEntry> matches)
        => new(ruleId, StateOf(check, matches.Count), matches.Count, 0, FirstPid(matches), BlockReason.None);

    internal static RuleStatus Pinned(Guid ruleId, PinOutcome outcome)
    {
        if (outcome.Matched == 0)
            return new RuleStatus(ruleId, RuleState.Idle, 0, 0, 0, BlockReason.None);

        return outcome.Affected == outcome.Matched
            ? new RuleStatus(ruleId, RuleState.Applied, outcome.Matched, outcome.Affected,
                             outcome.FirstPid, BlockReason.None)
            : new RuleStatus(ruleId, RuleState.Blocked, outcome.Matched, outcome.Affected,
                             outcome.FirstPid, outcome.Reason);
    }

    private static RuleState StateOf(RuleCheck check, int matched) => check switch
    {
        RuleCheck.Disabled => RuleState.Disabled,
        RuleCheck.Invalid => RuleState.Invalid,
        _ => matched == 0 ? RuleState.Idle : RuleState.NoRestriction,
    };

    private static int FirstPid(IReadOnlyList<ProcessEntry> matches) => matches.Count == 0 ? 0 : matches[0].Pid;
}
