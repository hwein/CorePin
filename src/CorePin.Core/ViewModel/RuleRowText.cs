using CorePin.Core.Engine;
using CorePin.Core.Rules;

namespace CorePin.Core.ViewModel;

/// Symbol and second-line text of a rule row. Semantics only — no colours, no brushes.
public static class RuleRowText
{
    public const string NotRunning = "not running";
    public const string NoRestriction = "no restriction";
    public const string Disabled = "disabled";
    public const string NeedsAdminRights = "Needs admin rights";
    public const string BlockedByWindows = "Blocked by Windows";
    public const string NeedsReview = "Needs review";
    public const string DeleteConfirmation = "Delete? Del again · Esc no";

    public static char Symbol(RuleState state) => state switch
    {
        RuleState.Applied => '●',
        RuleState.Blocked or RuleState.Invalid => '◐',
        _ => '○',
    };

    /// A problem outranks the state text; everything else shows its own state.
    public static string SecondLine(RuleStatus status) => status.State switch
    {
        RuleState.Blocked => WithCount(
            status.Reason == BlockReason.NeedsAdminRights ? NeedsAdminRights : BlockedByWindows, status),
        RuleState.Invalid => NeedsReview,
        RuleState.Applied => status.MatchedProcesses > 1
            ? $"pinned · {status.MatchedProcesses} processes"
            : $"pinned · PID {status.FirstPid}",
        RuleState.Disabled => Disabled,
        RuleState.NoRestriction => NoRestriction,
        _ => NotRunning,
    };

    /// The tooltip half of a problem line; null where the line explains itself.
    public static string? Explanation(RuleStatus status) => status.State switch
    {
        RuleState.Blocked => status.Reason == BlockReason.NeedsAdminRights
            ? "This process runs with higher privileges. Start CorePin as administrator to pin it."
            : "Windows refused to change this process's affinity. "
              + "Store and Game Pass titles often run in a job object that forbids it.",
        RuleState.Invalid => "This selection doesn't fit the current CPU. Pick cores again.",
        _ => null,
    };

    /// The count names the processes in the worse state, not the pinned ones.
    private static string WithCount(string text, RuleStatus status)
        => status.MatchedProcesses > 1
            ? $"{text} · {status.MatchedProcesses - status.AffectedProcesses} of {status.MatchedProcesses}"
            : text;
}
