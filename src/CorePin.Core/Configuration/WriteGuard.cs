namespace CorePin.Core.Configuration;

public enum GuardReason { None, ConfigTooNew, ConfigUnreadable, DebugTopology }

/// Write and edit lock. Three lock states, not one.
public sealed record WriteGuard(bool CanEditRules, bool CanPersist, GuardReason Reason)
{
    public static readonly WriteGuard Open = new(true, true, GuardReason.None);
    public static readonly WriteGuard ReadOnly = new(false, false, GuardReason.ConfigTooNew);
    public static readonly WriteGuard Unreadable = new(false, false, GuardReason.ConfigUnreadable);
    public static readonly WriteGuard NoPersist = new(true, false, GuardReason.DebugTopology);

    /// Field-wise AND — the cases can occur together; Reason follows the first cause by Rank.
    public static WriteGuard Strictest(WriteGuard a, WriteGuard b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return new WriteGuard(
            a.CanEditRules && b.CanEditRules,
            a.CanPersist && b.CanPersist,
            Rank(a.Reason) <= Rank(b.Reason) ? a.Reason : b.Reason);
    }

    private static int Rank(GuardReason reason) => reason switch
    {
        GuardReason.ConfigTooNew => 0,
        GuardReason.ConfigUnreadable => 1,
        GuardReason.DebugTopology => 2,
        _ => 3,
    };
}
