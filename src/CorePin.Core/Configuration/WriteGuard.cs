namespace CorePin.Core.Configuration;

public enum GuardReason { None, ConfigTooNew, ConfigUnreadable, DebugTopology }

/// Write and edit lock (S01 §3.6, S05 §7). Three lock states, not one.
public sealed record WriteGuard(bool CanEditRules, bool CanPersist, GuardReason Reason)
{
    public static readonly WriteGuard Open = new(true, true, GuardReason.None);
    public static readonly WriteGuard ReadOnly = new(false, false, GuardReason.ConfigTooNew);
    public static readonly WriteGuard Unreadable = new(false, false, GuardReason.ConfigUnreadable);
    public static readonly WriteGuard NoPersist = new(true, false, GuardReason.DebugTopology);

    /// The stricter one wins, field-wise AND: the cases can occur together
    /// (--debug-topology on a too new or unreadable config.json). Reason follows the first
    /// non-None cause in the order ConfigTooNew, ConfigUnreadable, DebugTopology, None
    /// (S05 §7.0).
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
