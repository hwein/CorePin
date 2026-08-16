using CorePin.Core.Configuration;

namespace CorePin.Tests;

/// S05 §7.0: the stricter guard wins field-wise, the reason follows the ranking
/// ConfigTooNew, ConfigUnreadable, DebugTopology, None.
public static class WriteGuardTests
{
    public static void Test_Strictest_CombinesFieldsWithAnd()
    {
        var combined = WriteGuard.Strictest(WriteGuard.Open, WriteGuard.NoPersist);

        Assert.True(combined.CanEditRules, "NoPersist leaves editing allowed (criterion 3)");
        Assert.True(!combined.CanPersist, "NoPersist forbids persisting");
        Assert.Equal(GuardReason.DebugTopology, combined.Reason, "the only non-None reason wins");
    }

    public static void Test_Strictest_RankingOfOverlappingReasons()
    {
        Assert.Equal(GuardReason.ConfigTooNew,
            WriteGuard.Strictest(WriteGuard.NoPersist, WriteGuard.ReadOnly).Reason,
            "ConfigTooNew outranks DebugTopology");

        Assert.Equal(GuardReason.ConfigUnreadable,
            WriteGuard.Strictest(WriteGuard.NoPersist, WriteGuard.Unreadable).Reason,
            "ConfigUnreadable outranks DebugTopology — only the latter allows editing");

        Assert.Equal(GuardReason.ConfigTooNew,
            WriteGuard.Strictest(WriteGuard.Unreadable, WriteGuard.ReadOnly).Reason,
            "ConfigTooNew outranks ConfigUnreadable");

        var strictest = WriteGuard.Strictest(WriteGuard.NoPersist, WriteGuard.ReadOnly);
        Assert.True(!strictest.CanEditRules && !strictest.CanPersist, "both fields are ANDed");
    }

    public static void Test_Strictest_OpenIsNeutral()
    {
        Assert.Equal(WriteGuard.Open, WriteGuard.Strictest(WriteGuard.Open, WriteGuard.Open),
            "two open guards stay open");
    }
}
