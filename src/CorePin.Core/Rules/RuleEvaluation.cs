using CorePin.Core.Primitives;

namespace CorePin.Core.Rules;

public enum RuleCheck { Apply, NoRestriction, Invalid, Disabled }

public static class RuleEvaluation
{
    /// NeedsReview comes FIRST: after a CPU change even a still-fitting mask must be Invalid.
    public static RuleCheck Check(Rule rule, AffinityMask machineMask)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (!rule.Enabled) return RuleCheck.Disabled;
        if (rule.NeedsReview) return RuleCheck.Invalid;
        if (rule.Threads == machineMask) return RuleCheck.NoRestriction;
        if (!rule.Threads.FitsInto(machineMask)) return RuleCheck.Invalid;
        return RuleCheck.Apply;
    }
}
