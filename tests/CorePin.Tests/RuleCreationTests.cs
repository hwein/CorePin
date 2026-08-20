using CorePin.Core.Rules;

namespace CorePin.Tests;

/// The two pure functions behind the picker: the process list and the rule it creates.
public static class RuleCreationTests
{
    private const string ExistingPath = @"C:\Games\Steam\steam.exe";
    private const string OtherPath = @"D:\Copy\steam.exe";

    public static void Test_ProcessRowBuilder_DedupKeepsLowestPid()
    {
        var rows = ProcessRowBuilder.Build(
        [
            new ProcessRow(4412, "steam.exe"),
            new ProcessRow(118, "STEAM.EXE"),
            new ProcessRow(7708, "Steam.exe"),
        ]);

        Assert.Equal(1, rows.Count, "one line per exe name, compared OrdinalIgnoreCase");
        Assert.Equal(118, rows[0].Pid, "the lowest pid of the group is the one shown");
    }

    public static void Test_ProcessRowBuilder_SortsOrdinalIgnoreCase()
    {
        var rows = ProcessRowBuilder.Build(
        [
            new ProcessRow(3, "Zeta.exe"),
            new ProcessRow(1, "alpha.exe"),
            new ProcessRow(2, "Mid.exe"),
        ]);

        Assert.Equal("alpha.exe,Mid.exe,Zeta.exe", string.Join(",", rows.Select(r => r.ExeName)),
            "alphabetical, independent of case");
    }

    public static void Test_CreateOrSelect_DuplicateSelectsExisting_IsNewFalse()
    {
        var set = Fixtures.Set(Fixtures.Rule(Fixtures.IdA, "steam.exe", Fixtures.FirstHalf));

        var result = RuleCreation.CreateOrSelect(
            set, canEditRules: true, Fixtures.Machine, "STEAM.EXE", OtherPath);

        Assert.True(!result.IsNew, "a second rule for the same exe selects the first one instead");
        Assert.True(result.Rule is not null, "and hands that first one back");
        // The Assert.True above already proved this is not null.
        Assert.Equal(Fixtures.IdA, result.Rule!.Id, "no new id is minted for a duplicate");
    }

    public static void Test_CreateOrSelect_ExistingKeepsLastKnownPath()
    {
        var set = Fixtures.Set(
            Fixtures.Rule(Fixtures.IdA, "steam.exe", Fixtures.FirstHalf) with { LastKnownPath = ExistingPath });

        var result = RuleCreation.CreateOrSelect(
            set, canEditRules: true, Fixtures.Machine, "steam.exe", OtherPath);

        Assert.True(result.Rule is not null, "the existing rule comes back");
        // The Assert.True above already proved this is not null.
        Assert.Equal(ExistingPath, result.Rule!.LastKnownPath, "the path it already had is not overwritten");
    }

    public static void Test_CreateOrSelect_New_AllThreadsEnabled_IsNewTrue()
    {
        var result = RuleCreation.CreateOrSelect(
            RuleSet.Empty, canEditRules: true, Fixtures.Machine, "cyberpunk2077.exe", ExistingPath);

        Assert.True(result.IsNew, "nothing matched, so a rule is created");
        Assert.True(result.Rule is not null, "and it comes back with the result");
        // The Assert.True above already proved this is not null.
        Assert.Equal(Fixtures.Machine, result.Rule!.Threads, "a new rule starts with every core marked");
        Assert.True(result.Rule.Enabled, "and enabled");
        Assert.Equal("cyberpunk2077.exe", result.Rule.ExeName, "under the name that was picked");
        Assert.Equal(ExistingPath, result.Rule.LastKnownPath, "carrying the path if one was known");
        Assert.True(result.Rule.Id != Guid.Empty, "with an id of its own");
    }

    public static void Test_CreateOrSelect_CanEditRulesFalse_RuleNull()
    {
        var set = Fixtures.Set(Fixtures.Rule(Fixtures.IdA, "steam.exe", Fixtures.FirstHalf));

        var result = RuleCreation.CreateOrSelect(
            set, canEditRules: false, Fixtures.Machine, "notepad.exe", ExistingPath);

        Assert.True(result.Rule is null, "a locked list creates nothing");
        Assert.True(!result.IsNew, "and reports nothing as new");
        Assert.Equal(1, set.Rules.Count, "the rule set is untouched");
    }

    public static void Test_RulePatch_OnCurrentVersion_KeepsInterimChange()
    {
        var created = Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.Machine);
        var set = RuleSet.Empty.With(created);

        set = set.With(created with { Threads = Fixtures.FirstHalf });
        var current = set.ById(Fixtures.IdA);

        Assert.True(current is not null, "the rule is there when the late path arrives");
        // The Assert.True above already proved this is not null.
        set = set.With(current! with { LastKnownPath = ExistingPath });
        var patched = set.ById(Fixtures.IdA);

        Assert.True(patched is not null, "and it survives the patch");
        // The Assert.True above already proved this is not null.
        Assert.Equal(Fixtures.FirstHalf, patched!.Threads, "the selection made in between is kept");
        Assert.Equal(ExistingPath, patched.LastKnownPath, "only the path changes");
        Assert.Equal(1, set.Rules.Count, "and no second copy appears");
    }

    public static void Test_RulePatch_ByIdAfterWithout_ReturnsNull()
    {
        var set = RuleSet.Empty.With(Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.Machine));

        var remaining = set.Without(Fixtures.IdA);

        Assert.True(remaining.ById(Fixtures.IdA) is null, "a deleted rule cannot be read back");
        Assert.Equal(0, remaining.Rules.Count, "and the set stays without it");
    }
}
