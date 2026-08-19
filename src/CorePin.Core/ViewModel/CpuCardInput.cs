using CorePin.Core.Primitives;

namespace CorePin.Core.ViewModel;

/// Everything the CPU card needs about the selected rule — it cannot reach the rule set.
public sealed record CpuCardInput(
    AffinityMask Threads,
    bool ShowSelection,
    bool CanEditRules,
    bool RuleEnabled);
