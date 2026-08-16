using CorePin.Core.Primitives;

namespace CorePin.Core.Rules;

public enum RuleCheck { Apply, NoRestriction, Invalid, Disabled }

public static class RuleEvaluation
{
    /// Binding order (S01 §3.4):
    ///   1. !Enabled                       -> Disabled
    ///   2. NeedsReview                    -> Invalid        (02 §6, the CPU changed)
    ///   3. Threads == machineMask         -> NoRestriction  (02 §5.1/§5.3)
    ///   4. !Threads.FitsInto(machineMask) -> Invalid        (02 §5.4)
    ///   5. otherwise                      -> Apply
    /// NeedsReview stands BEFORE the mask checks: after a CPU change every rule has to be
    /// Invalid, including one whose mask happens to still fit.
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
