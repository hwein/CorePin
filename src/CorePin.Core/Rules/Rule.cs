using CorePin.Core.Primitives;

namespace CorePin.Core.Rules;

public sealed record Rule
{
    public required Guid Id { get; init; }

    /// Compared OrdinalIgnoreCase.
    public required string ExeName { get; init; }

    /// Display only, never a comparison basis.
    public string? LastKnownPath { get; init; }

    public required AffinityMask Threads { get; init; }

    public bool Enabled { get; init; } = true;

    /// NOT persisted. Set after loading, cleared when the user re-picks this rule.
    public bool NeedsReview { get; init; }
}
