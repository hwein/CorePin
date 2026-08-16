using CorePin.Core.Primitives;

namespace CorePin.Core.Rules;

public sealed record Rule
{
    public required Guid Id { get; init; }

    /// Compared OrdinalIgnoreCase (S01 §3.4).
    public required string ExeName { get; init; }

    /// Display only, never a comparison basis (02 §5.1).
    public string? LastKnownPath { get; init; }

    public required AffinityMask Threads { get; init; }

    public bool Enabled { get; init; } = true;

    /// NOT persisted (02 §5.5). Set by the composition root after loading (S01 §3.7),
    /// because only it knows the loaded count AND the measured topology; cleared by
    /// S09/S10 as soon as the user re-picks the selection of this rule.
    public bool NeedsReview { get; init; }
}
