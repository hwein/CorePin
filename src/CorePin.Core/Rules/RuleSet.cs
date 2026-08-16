namespace CorePin.Core.Rules;

/// Immutable, copied on construction (S01 §3.4).
public sealed class RuleSet
{
    private readonly List<Rule> _rules;

    public RuleSet(IEnumerable<Rule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = [.. rules];
    }

    public static RuleSet Empty { get; } = new(Array.Empty<Rule>());

    public IReadOnlyList<Rule> Rules => _rules;

    public Rule? ById(Guid id) => _rules.FirstOrDefault(r => r.Id == id);

    /// Replaces the rule with the same id, or appends it.
    public RuleSet With(Rule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var copy = new List<Rule>(_rules);
        int index = copy.FindIndex(r => r.Id == rule.Id);
        if (index < 0) copy.Add(rule); else copy[index] = rule;
        return new RuleSet(copy);
    }

    public RuleSet Without(Guid id) => new(_rules.Where(r => r.Id != id));

    /// Sets NeedsReview on ALL rules (02 §6). Caller: only S01 §3.7, step 4b.
    public RuleSet MarkAllForReview() => new(_rules.Select(r => r with { NeedsReview = true }));
}
