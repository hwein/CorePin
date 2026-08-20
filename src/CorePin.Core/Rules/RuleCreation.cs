using CorePin.Core.Primitives;

namespace CorePin.Core.Rules;

/// Rule is null only when editing is locked; IsNew decides whether the engine hears about it.
public readonly record struct RuleCreationResult(Rule? Rule, bool IsNew);

/// Both ways into a rule — the picker and the file dialog — end here.
public static class RuleCreation
{
    /// canEditRules instead of the write guard: this module only ever sees Primitives.
    public static RuleCreationResult CreateOrSelect(
        RuleSet current, bool canEditRules, AffinityMask machineMask,
        string exeName, string? lastKnownPath)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(exeName);

        if (!canEditRules) return new RuleCreationResult(null, false);

        var existing = current.Rules.FirstOrDefault(
            r => string.Equals(r.ExeName, exeName, StringComparison.OrdinalIgnoreCase));

        // One program, one rule: the existing one is selected, and keeps its own path.
        if (existing is not null) return new RuleCreationResult(existing, IsNew: false);

        var rule = new Rule
        {
            Id = Guid.NewGuid(),
            ExeName = exeName,
            LastKnownPath = lastKnownPath,
            Threads = machineMask,
            Enabled = true,
        };
        return new RuleCreationResult(rule, IsNew: true);
    }
}
