using CorePin.Core.Configuration;
using CorePin.Core.Diagnostics;
using CorePin.Core.Engine;
using CorePin.Core.Primitives;
using CorePin.Core.Rules;
using CorePin.Core.ViewModel;
using CorePin.Tests.Fakes;

namespace CorePin.Tests;

/// The window state without a window: rows, status line, banners, locks, delete, saving.
public static class RuleListViewModelTests
{
    private const string LatePath = @"C:\Games\a.exe";

    private static readonly Guid IdC = new("cccccccc-0000-0000-0000-000000000003");
    private static readonly DateTime Tick = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);

    public static void Test_Sorting_AlphabeticalRegardlessOfState()
    {
        var harness = new Harness(Fixtures.Set(
            Fixtures.Rule(Fixtures.IdB, "Zeta.exe", Fixtures.FirstHalf),
            Fixtures.Rule(Fixtures.IdA, "alpha.exe", Fixtures.FirstHalf),
            Fixtures.Rule(IdC, "Mid.exe", Fixtures.FirstHalf)));

        Assert.Equal("alpha.exe,Mid.exe,Zeta.exe", Names(harness), "sorted OrdinalIgnoreCase by exe name");

        harness.Model.ApplyStatus([Status(Fixtures.IdA, RuleState.Applied, 1, 1, 42)]);

        Assert.Equal("alpha.exe,Mid.exe,Zeta.exe", Names(harness), "a live state change never re-sorts");
    }

    public static void Test_SecondLine_ProblemReasonBeatsStateText()
    {
        var harness = OneRule();

        harness.Model.ApplyStatus([Status(Fixtures.IdA, RuleState.Blocked, 1, 0, 42, BlockReason.NeedsAdminRights)]);

        Assert.Equal("Needs admin rights", harness.Model.Rows[0].SecondLine, "the problem beats the state text");
    }

    public static void Test_SecondLine_BlockedBeatsEnabledFalse()
    {
        var harness = new Harness(Fixtures.Set(
            Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf) with { Enabled = false }));
        var row = harness.Model.Rows[0];

        Assert.Equal("disabled", row.SecondLine, "before the engine reports, the rule speaks for itself");

        harness.Model.ApplyStatus([Status(Fixtures.IdA, RuleState.Blocked, 1, 0, 42, BlockReason.BlockedByWindows)]);

        Assert.Equal("Blocked by Windows", row.SecondLine, "a failed release stays visible");
        Assert.Equal('◐', row.Symbol, "amber, not grey");
        Assert.True(!row.IsDimmed, "amber wins over the dimming");
    }

    public static void Test_StatusChanged_MergedByRuleId()
    {
        var harness = TwoRules();
        harness.Model.ApplyStatus(
        [
            Status(Fixtures.IdA, RuleState.Applied, 1, 1, 42),
            Status(Fixtures.IdB, RuleState.Applied, 1, 1, 43),
        ]);

        harness.Model.ApplyStatus([Status(Fixtures.IdA, RuleState.Idle)]);

        Assert.Equal(RuleState.Idle, Row(harness, Fixtures.IdA).State, "the named rule follows the update");
        Assert.Equal(RuleState.Applied, Row(harness, Fixtures.IdB).State, "the other rule keeps its last state");
    }

    public static void Test_StatusChanged_UnknownRuleIdIsIgnored()
    {
        var harness = OneRule();

        harness.Model.ApplyStatus([Status(Guid.NewGuid(), RuleState.Applied, 1, 1, 42)]);

        Assert.Equal(1, harness.Model.Rows.Count, "no row is created for a rule that is gone");
        Assert.Equal(RuleState.Idle, harness.Model.Rows[0].State, "the existing row is untouched");
    }

    public static void Test_StatusLine_ThirdFormOnlyWhileAppliedIsLive()
    {
        var harness = TwoRules();
        harness.Model.ApplyStatus([Status(Fixtures.IdA, RuleState.Applied, 1, 1, 42)]);
        harness.Model.ApplyHeartbeat(new EngineHeartbeat(Tick.AddSeconds(3), 2, "a.exe", Tick));

        Assert.Equal("Watching 2 rules · applied a.exe 3 s ago", harness.Model.StatusText, "one rule is applied");

        harness.Model.ApplyStatus([Status(Fixtures.IdA, RuleState.Idle)]);

        Assert.Equal("Watching 2 rules", harness.Model.StatusText,
            "the third form goes as soon as nothing is applied any more");
    }

    public static void Test_StatusLine_CountsDisabledRules()
    {
        var harness = new Harness(Fixtures.Set(
            Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf),
            Fixtures.Rule(Fixtures.IdB, "b.exe", Fixtures.FirstHalf),
            Fixtures.Rule(IdC, "c.exe", Fixtures.FirstHalf) with { Enabled = false }));

        Assert.Equal("Watching 3 rules", harness.Model.StatusText, "a disabled rule is still being watched over");
    }

    public static void Test_StatusLine_SavedSuffixHoldsUntilTheNextHeartbeat()
    {
        var harness = TwoRules();
        harness.Model.ApplyStatus([Status(Fixtures.IdA, RuleState.Applied, 1, 1, 42)]);
        harness.Model.ApplyHeartbeat(new EngineHeartbeat(Tick.AddSeconds(3), 2, "a.exe", Tick));

        harness.Model.NotifySaved();

        Assert.Equal("Watching 2 rules · applied a.exe 3 s ago · saved", harness.Model.StatusText,
            "the suffix hangs on the regular line");

        harness.Model.ApplyHeartbeat(new EngineHeartbeat(Tick.AddSeconds(4), 2, "a.exe", Tick));

        Assert.Equal("Watching 2 rules · applied a.exe 4 s ago", harness.Model.StatusText,
            "the next heartbeat takes it away, no timer of its own");
    }

    public static void Test_StatusLine_SavedSuffixNeverOnTheFaultedForm()
    {
        var harness = TwoRules();
        harness.Model.ApplyFault(new EngineFault("boom", 10));

        harness.Model.NotifySaved();

        Assert.Equal(StatusLine.Stopped, harness.Model.StatusText,
            "'saved' behind 'Stopped watching' would mislead");
    }

    public static void Test_Faulted_FourthStatusLineText()
    {
        var harness = TwoRules();

        harness.Model.ApplyFault(new EngineFault("boom", 10));

        Assert.Equal(StatusLine.Stopped, harness.Model.StatusText, "the fourth text takes over");

        harness.Model.ApplyHeartbeat(new EngineHeartbeat(Tick, 2, "a.exe", Tick));

        Assert.Equal(StatusLine.Stopped, harness.Model.StatusText, "and it stays for the rest of the session");
    }

    public static void Test_Faulted_BlocksEditing()
    {
        var harness = OneRule();
        harness.Model.SelectedRuleId = Fixtures.IdA;
        harness.Model.ApplyStatus([Status(Fixtures.IdA, RuleState.Applied, 1, 1, 42)]);

        harness.Model.ApplyFault(new EngineFault("boom", 10));
        harness.Model.ToggleSelectedEnabled();
        harness.Model.DeleteSelectedRule();
        harness.Model.SetSelection(Fixtures.IdA, Fixtures.SecondHalf);

        Assert.True(!harness.Model.IsEditable, "editing is locked independently of the write guard");
        Assert.Equal(0, harness.Submits.Count, "nothing reaches the dead worker thread");
        Assert.Equal(0, harness.Saves.Count, "and nothing is saved either");
        Assert.Equal("pinned · PID 42", harness.Model.Rows[0].SecondLine, "the last known row stays as it was");
        Assert.Equal(BannerText.EditingStopped, harness.Model.LockedTooltip, "the tooltip names the working way out");
    }

    public static void Test_Banner_CpuChangeStaysUntilTheLastRule()
    {
        var harness = new Harness(Fixtures.Set(
            Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf) with { NeedsReview = true },
            Fixtures.Rule(Fixtures.IdB, "b.exe", Fixtures.FirstHalf) with { NeedsReview = true },
            Fixtures.Rule(IdC, "c.exe", Fixtures.FirstHalf) with { NeedsReview = true }));

        Assert.True(harness.Model.Banners.Contains(BannerText.CpuChanged), "three rules wait for a review");

        harness.Model.SetSelection(Fixtures.IdA, Fixtures.TwoThreads);
        harness.Model.SetSelection(Fixtures.IdB, Fixtures.TwoThreads);

        Assert.True(harness.Model.Banners.Contains(BannerText.CpuChanged), "one rule is still waiting");

        harness.Model.SetSelection(IdC, Fixtures.TwoThreads);

        Assert.Equal(0, harness.Model.Banners.Count, "the last review clears the banner");
    }

    public static void Test_Banner_SurvivesRestartWhileNeedsReview()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path, new RecordingLog(), new FakeClock());
        var harness = new Harness(
            Fixtures.Set(
                Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf) with { NeedsReview = true },
                Fixtures.Rule(Fixtures.IdB, "b.exe", Fixtures.FirstHalf) with { NeedsReview = true }),
            loadedMachine: new MachineInfo("Test CPU", 16));

        harness.Model.SetSelection(Fixtures.IdA, Fixtures.TwoThreads);
        store.Save(harness.Saves[^1]);

        var loaded = store.Load();
        // The composition root's own step after a load: compare, then mark.
        var marked = loaded.Config.Machine.LogicalProcessors == 8
            ? loaded.RawRules
            : loaded.RawRules.MarkAllForReview();
        var restarted = new Harness(marked, loadedMachine: loaded.Config.Machine);

        Assert.Equal(16, loaded.Config.Machine.LogicalProcessors, "the old count survives an unfinished review");
        Assert.True(restarted.Model.Banners.Contains(BannerText.CpuChanged), "the banner is back after the restart");
    }

    public static void Test_NeedsReview_ClearedAtomicallyWithSelection()
    {
        var harness = new Harness(Fixtures.Set(
            Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf) with { NeedsReview = true }));

        harness.Model.SetSelection(Fixtures.IdA, Fixtures.SecondHalf);

        Assert.Equal(1, harness.Submits.Count, "the engine sees exactly one step");

        var submitted = harness.Submits[^1].Rules.ById(Fixtures.IdA);

        Assert.True(submitted is not null, "the submitted set still holds the rule");
        // The Assert.True above already proved this is not null.
        Assert.Equal(Fixtures.SecondHalf, submitted!.Threads, "the new selection is in the submitted set");
        Assert.True(!submitted.NeedsReview, "and the review flag is already cleared in the same set");
        Assert.Equal(RuleChangeKind.SelectionChanged, harness.Submits[^1].Change.Kind, "the change kind fits");
    }

    public static void Test_Banner_CorruptShowsRenamedFile()
    {
        var harness = new Harness(RuleSet.Empty,
            outcome: ConfigLoadOutcome.Corrupt, detail: "config.corrupt-20260815-101500.json");

        Assert.Equal(
            "config.json couldn't be read and was reset — "
            + "your previous file was saved as config.corrupt-20260815-101500.json.",
            harness.Model.Banners[0], "the banner shows the name the store chose");
    }

    public static void Test_Banner_CorruptWithoutDetail()
    {
        var harness = new Harness(RuleSet.Empty, outcome: ConfigLoadOutcome.Corrupt, detail: null);

        Assert.Equal("config.json couldn't be read and was reset.",
            harness.Model.Banners[0], "no rename to name means no file name in the banner");
    }

    public static void Test_Banner_RulesSkippedUsesFixedFileName()
    {
        var harness = new Harness(OneRuleSet(), detail: ConfigFileNames.Skipped, rulesSkipped: 2);

        Assert.Equal("2 rule(s) could not be loaded and were skipped — see config.skipped.json in the CorePin folder.",
            harness.Model.Banners[0], "the fixed side file name is taken over, not rebuilt");
    }

    public static void Test_Banner_RulesSkippedWithoutDetail()
    {
        var harness = new Harness(OneRuleSet(), detail: null, rulesSkipped: 2);

        Assert.Equal("2 rule(s) could not be loaded and were skipped.",
            harness.Model.Banners[0], "no side file to name means no 'see' clause");
    }

    public static void Test_Banner_OneBannerFromStrictestReason()
    {
        var guard = WriteGuard.Strictest(WriteGuard.ReadOnly, WriteGuard.NoPersist);
        var harness = new Harness(RuleSet.Empty, guard: guard);

        Assert.Equal(GuardReason.ConfigTooNew, guard.Reason, "the stricter reason wins before the view model sees it");
        Assert.Equal(1, harness.Model.Banners.Count, "exactly one banner, never one per cause");
        Assert.Equal(BannerText.ConfigTooNew, harness.Model.Banners[0], "and it is the one the reason names");
    }

    public static void Test_WriteGuard_ReadOnlyBlocksInput()
    {
        var harness = OneRule(guard: WriteGuard.ReadOnly);
        harness.Model.SelectedRuleId = Fixtures.IdA;

        harness.Model.ToggleSelectedEnabled();
        harness.Model.DeleteSelectedRule();
        harness.Model.AddRule(Fixtures.Rule(Fixtures.IdB, "b.exe", Fixtures.FirstHalf));

        Assert.Equal(0, harness.Submits.Count, "no edit reaches the engine");
        Assert.Equal(0, harness.Saves.Count, "and none asks for a save");
        Assert.Equal(1, harness.Model.Rows.Count, "the list is unchanged");
        Assert.True(!harness.Model.HasPendingDeleteConfirmation, "Del does not even arm the confirmation");
        Assert.True(harness.Model.SelectedInput is not null, "the card stays readable");
        Assert.Equal(BannerText.ConfigTooNew, harness.Model.LockedTooltip, "the locked tooltip repeats the banner");
    }

    public static void Test_WriteGuard_NoPersistAllowsEditing()
    {
        var harness = OneRule(guard: WriteGuard.NoPersist);
        harness.Model.SelectedRuleId = Fixtures.IdA;

        harness.Model.SetSelection(Fixtures.IdA, Fixtures.SecondHalf);

        Assert.Equal(1, harness.Submits.Count, "pinning works unchanged");
        Assert.Equal(1, harness.Saves.Count, "the save is requested; the store discards it on its own");
        Assert.True(harness.Model.LockedTooltip is null, "nothing is locked");
        Assert.Equal(StatusLine.DebugTopologyHint, harness.Model.DebugTopologyHint, "only a low-key hint");
        Assert.Equal(0, harness.Model.Banners.Count, "and no banner");
    }

    public static void Test_Delete_SecondPressRemovesTheRule()
    {
        var harness = OneRule();
        harness.Model.SelectedRuleId = Fixtures.IdA;

        harness.Model.DeleteSelectedRule();

        Assert.True(harness.Model.HasPendingDeleteConfirmation, "the first press only arms the confirmation");
        Assert.Equal(RuleRowText.DeleteConfirmation, harness.Model.Rows[0].SecondLine, "the row asks");
        Assert.Equal(0, harness.Submits.Count, "nothing is deleted yet");

        harness.Model.DeleteSelectedRule();

        Assert.Equal(0, harness.Model.Rows.Count, "the second press deletes");
        Assert.Equal(RuleChangeKind.Removed, harness.Submits[^1].Change.Kind, "the engine hears Removed");
        Assert.True(harness.Submits[^1].Rules.ById(Fixtures.IdA) is null, "and gets the set without the rule");
        Assert.Equal(1, harness.Saves.Count, "the deletion is saved");
    }

    public static void Test_Delete_StatusUpdateKeepsConfirmationText()
    {
        var harness = OneRule();
        harness.Model.SelectedRuleId = Fixtures.IdA;
        harness.Model.DeleteSelectedRule();

        harness.Model.ApplyStatus([Status(Fixtures.IdA, RuleState.Applied, 1, 1, 42)]);

        Assert.Equal(RuleRowText.DeleteConfirmation, harness.Model.Rows[0].SecondLine, "a tick never overwrites it");
        Assert.Equal('●', harness.Model.Rows[0].Symbol, "only dot and state follow the tick");
    }

    public static void Test_Delete_CancelConfirmationLeavesRuleUntouched()
    {
        var harness = OneRule();
        harness.Model.SelectedRuleId = Fixtures.IdA;
        harness.Model.DeleteSelectedRule();

        harness.Model.CancelPendingDeleteConfirmation();

        Assert.True(!harness.Model.HasPendingDeleteConfirmation, "the confirmation is gone");
        Assert.Equal(1, harness.Model.Rows.Count, "the rule is still there");
        Assert.Equal("not running", harness.Model.Rows[0].SecondLine, "and the row is back to its state text");
        Assert.Equal(0, harness.Submits.Count, "no engine call");
        Assert.Equal(0, harness.Saves.Count, "no save call");
    }

    public static void Test_Delete_SelectionChangeCancelsConfirmation()
    {
        var harness = TwoRules();
        harness.Model.SelectedRuleId = Fixtures.IdA;
        harness.Model.DeleteSelectedRule();

        harness.Model.SelectedRuleId = Fixtures.IdB;

        Assert.True(!harness.Model.HasPendingDeleteConfirmation, "moving the selection cancels");
        Assert.Equal(2, harness.Model.Rows.Count, "nothing was deleted");
        Assert.Equal("not running", Row(harness, Fixtures.IdA).SecondLine, "the armed row is plain again");
    }

    public static void Test_Esc_DeleteConfirmationOutranksFlyout()
    {
        // The flyout itself comes later; what this model owes its handler is this pair.
        var harness = OneRule();
        harness.Model.SelectedRuleId = Fixtures.IdA;
        harness.Model.DeleteSelectedRule();

        Assert.True(harness.Model.HasPendingDeleteConfirmation, "the flyout handler can see rank one");

        harness.Model.CancelPendingDeleteConfirmation();

        Assert.True(!harness.Model.HasPendingDeleteConfirmation, "and cancel it instead of closing itself");

        harness.Model.CancelPendingDeleteConfirmation();

        Assert.Equal(1, harness.Model.Rows.Count, "a second cancel is a no-op, not a deletion");
    }

    public static void Test_CpuCardInput_ShowSelectionFromRuleEvaluationCheck()
    {
        var harness = new Harness(Fixtures.Set(
            Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf) with { NeedsReview = true }));
        harness.Model.SelectedRuleId = Fixtures.IdA;
        harness.Model.ApplyStatus([Status(Fixtures.IdA, RuleState.Applied, 1, 1, 42)]);

        var input = harness.Model.SelectedInput;

        Assert.True(input is not null, "a selected rule always has card input");
        // The Assert.True above already proved this is not null.
        Assert.True(!input!.ShowSelection, "the check decides, not the last reported state");
        Assert.Equal(AffinityMask.Empty, input.Threads, "without a selection to show there is no mask");
    }

    public static void Test_CpuCardInput_ShowSelectionFalseWhenDisabledAndNeedsReview()
    {
        var harness = new Harness(Fixtures.Set(
            Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf) with { Enabled = false, NeedsReview = true }));
        harness.Model.SelectedRuleId = Fixtures.IdA;

        var input = harness.Model.SelectedInput;

        Assert.True(input is not null, "a selected rule always has card input");
        // The Assert.True above already proved this is not null.
        Assert.True(!input!.ShowSelection, "a disabled rule after a CPU change must not show stale indices");
        Assert.Equal(AffinityMask.Empty, input.Threads, "no selection to show means no mask");
    }

    public static void Test_CpuCardInput_CanEditRulesFollowsFaulted()
    {
        var harness = OneRule();
        harness.Model.SelectedRuleId = Fixtures.IdA;

        Assert.True(harness.Model.SelectedInput?.CanEditRules == true, "an open guard allows card clicks");

        harness.Model.ApplyFault(new EngineFault("boom", 10));

        Assert.True(harness.Model.SelectedInput?.CanEditRules == false, "a fault takes them away");
        Assert.Equal((Guid?)Fixtures.IdA, harness.Model.SelectedRuleId, "without the selection moving");
    }

    public static void Test_EmptyState_ListAndCardAreSeparate()
    {
        var harness = new Harness(RuleSet.Empty);

        Assert.Equal(0, harness.Model.Rows.Count, "no rows");
        Assert.True(!harness.Model.HasRules, "the list shows its placeholder sentence");
        Assert.Equal("No rules yet — add an app to get started.", RuleListViewModel.EmptyListText, "wording");
        Assert.True(harness.Model.SelectedRuleId is null, "nothing is preselected");
        Assert.True(harness.Model.SelectedInput is null, "so the card has no rule to edit");
        Assert.Equal(StatusLine.NoRules, harness.Model.StatusText, "and the status line says so");
    }

    public static void Test_Selection_NewRuleBecomesSelected()
    {
        var harness = new Harness(RuleSet.Empty);

        harness.Model.AddRule(Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.Machine));

        Assert.Equal((Guid?)Fixtures.IdA, harness.Model.SelectedRuleId, "a new rule is selected right away");
        Assert.Equal(1, harness.Model.Rows.Count, "and appears in the list");
        Assert.Equal(RuleChangeKind.Added, harness.Submits[^1].Change.Kind, "the engine hears Added");
        Assert.Equal(1, harness.Saves.Count, "and the rule is saved");
    }

    public static void Test_Selection_DeleteResetsToNull()
    {
        var harness = OneRule();
        harness.Model.SelectedRuleId = Fixtures.IdA;

        harness.Model.DeleteSelectedRule();
        harness.Model.DeleteSelectedRule();

        Assert.True(harness.Model.SelectedRuleId is null, "the deleted rule leaves no selection behind");
        Assert.True(harness.Model.SelectedInput is null, "and the card falls back to plain hardware");
    }

    public static void Test_AddOrSelect_NewRuleCreatedSelectedSubmitted()
    {
        var harness = new Harness(RuleSet.Empty);

        var result = harness.Model.AddOrSelect("cyberpunk2077.exe", LatePath);

        Assert.True(result.IsNew, "nothing matched, so the picker created a rule");
        Assert.True(result.Rule is not null, "and hands it back for the icon wiring");
        // The Assert.True above already proved this is not null.
        Assert.Equal((Guid?)result.Rule!.Id, harness.Model.SelectedRuleId, "the new rule is selected");
        Assert.Equal(LatePath, result.Rule.LastKnownPath, "with the path it was handed");
        Assert.Equal(1, harness.Model.Rows.Count, "and it is in the list");
        Assert.Equal(RuleChangeKind.Added, harness.Submits[^1].Change.Kind, "the engine hears Added");
        Assert.Equal(1, harness.Saves.Count, "and it is saved");
    }

    public static void Test_AddOrSelect_DuplicateOnlyMovesSelection()
    {
        var harness = TwoRules();
        harness.Model.SelectedRuleId = Fixtures.IdB;
        int submits = harness.Submits.Count;
        int saves = harness.Saves.Count;

        var result = harness.Model.AddOrSelect("A.EXE", LatePath);

        Assert.True(!result.IsNew, "the existing rule is picked instead of a second one");
        Assert.True(result.Rule is not null, "and comes back for the caller's icon wiring");
        Assert.Equal((Guid?)Fixtures.IdA, harness.Model.SelectedRuleId, "the selection jumps to it");
        Assert.Equal(2, harness.Model.Rows.Count, "no second row appears");
        // The Assert.True above already proved this is not null.
        Assert.True(result.Rule!.LastKnownPath is null, "the existing rule keeps its own path");
        Assert.Equal(submits, harness.Submits.Count, "the engine hears nothing");
        Assert.Equal(saves, harness.Saves.Count, "and nothing is saved");
    }

    public static void Test_AddOrSelect_FaultedDoesNothing()
    {
        var harness = OneRule();
        harness.Model.ApplyFault(new EngineFault("boom", 10));
        int submits = harness.Submits.Count;
        int saves = harness.Saves.Count;

        var result = harness.Model.AddOrSelect("b.exe", null);

        Assert.True(result.Rule is null, "locked editing creates nothing");
        Assert.True(!result.IsNew, "and reports nothing as new");
        Assert.Equal(1, harness.Model.Rows.Count, "the list is unchanged");
        Assert.True(harness.Model.SelectedRuleId is null, "not even the selection moves");
        Assert.Equal(submits, harness.Submits.Count, "no submit");
        Assert.Equal(saves, harness.Saves.Count, "no save");
    }

    public static void Test_PatchLastKnownPath_KeepsInterimChanges()
    {
        var harness = new Harness(RuleSet.Empty);
        harness.Model.AddRule(Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.Machine));
        harness.Model.SetSelection(Fixtures.IdA, Fixtures.TwoThreads);
        int submits = harness.Submits.Count;
        int saves = harness.Saves.Count;

        harness.Model.PatchLastKnownPath(Fixtures.IdA, LatePath);

        var patched = harness.Saves[^1].Rules.ById(Fixtures.IdA);

        Assert.True(patched is not null, "the rule is still in the authoritative set");
        // The Assert.True above already proved this is not null.
        Assert.Equal(Fixtures.TwoThreads, patched!.Threads, "the selection made meanwhile is not rolled back");
        Assert.Equal(LatePath, patched.LastKnownPath, "and the late path is now part of the set");
        Assert.Equal(submits, harness.Submits.Count, "a display-only field never reaches the engine");
        Assert.Equal(saves + 1, harness.Saves.Count, "but it is saved like any other change");
        Assert.True(Row(harness, Fixtures.IdA).Tooltip.Contains(LatePath, StringComparison.Ordinal),
            "and the row shows the path it just learned");
    }

    public static void Test_PatchLastKnownPath_NoOpWhenRuleDeleted()
    {
        var harness = new Harness(RuleSet.Empty);
        harness.Model.AddRule(Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.Machine));
        harness.Model.DeleteSelectedRule();
        harness.Model.DeleteSelectedRule();
        int submits = harness.Submits.Count;
        int saves = harness.Saves.Count;

        harness.Model.PatchLastKnownPath(Fixtures.IdA, LatePath);

        Assert.Equal(saves, harness.Saves.Count, "a late path does not write a deleted rule back");
        Assert.Equal(submits, harness.Submits.Count, "and the engine hears nothing either");
        Assert.True(harness.Saves[^1].Rules.ById(Fixtures.IdA) is null, "the rule stays gone");
    }

    public static void Test_Save_BuiltFromTheMarkedRuleSet()
    {
        var harness = new Harness(
            Fixtures.Set(
                Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf) with { NeedsReview = true },
                Fixtures.Rule(Fixtures.IdB, "b.exe", Fixtures.FirstHalf)),
            loadedMachine: new MachineInfo("Old CPU", 16),
            measuredMachine: new MachineInfo("New CPU", 8));

        harness.Model.UpdateWindowBounds(new WindowBounds(10, 20, 660, 480));

        Assert.Equal("Old CPU", harness.Saves[^1].Machine.CpuName, "the old name stays while a rule is marked");
        Assert.Equal(16, harness.Saves[^1].Machine.LogicalProcessors, "the old count stays while a rule is marked");
        Assert.Equal(2, harness.Saves[^1].Rules.Rules.Count, "the marked set is what gets written");

        harness.Model.SetSelection(Fixtures.IdA, Fixtures.TwoThreads);

        Assert.Equal("New CPU", harness.Saves[^1].Machine.CpuName, "once nothing is marked the measured name takes over");
        Assert.Equal(8, harness.Saves[^1].Machine.LogicalProcessors, "once nothing is marked the count moves on");
    }

    public static void Test_RoundTrip_MarkedRuleSetSurvivesRestart()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path, new RecordingLog(), new FakeClock());
        var harness = new Harness(
            Fixtures.Set(Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf) with { NeedsReview = true }),
            loadedMachine: new MachineInfo("Test CPU", 16));

        harness.Model.UpdateWindowBounds(new WindowBounds(10, 20, 660, 480));
        store.Save(harness.Saves[^1]);

        var loaded = store.Load();

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "the file we just wrote loads");
        Assert.Equal(16, loaded.Config.Machine.LogicalProcessors,
            "the saved file still carries the old count, not the measured one");
        Assert.Equal(1, loaded.RawRules.Rules.Count, "the marked rule itself is in the file");
        Assert.Equal(new WindowBounds(10, 20, 660, 480), loaded.Config.Settings.WindowBounds, "and so is the geometry");
    }

    public static void Test_WindowBounds_TrackedContinuously()
    {
        var harness = OneRule();

        harness.Model.UpdateWindowBounds(new WindowBounds(10, 20, 660, 480));

        Assert.Equal(new WindowBounds(10, 20, 660, 480), harness.Model.CurrentWindowBounds, "the field follows at once");
        Assert.Equal(new WindowBounds(10, 20, 660, 480), harness.Saves[^1].Settings.WindowBounds,
            "and a save is asked for right away, not only when the window closes");

        harness.Model.UpdateWindowBounds(new WindowBounds(11, 20, 700, 480));
        harness.Model.UpdateWindowBounds(new WindowBounds(11, 20, 700, 480));

        Assert.Equal(2, harness.Saves.Count, "an unchanged geometry asks for nothing");
        Assert.Equal(new WindowBounds(11, 20, 700, 480), harness.Model.CurrentWindowBounds,
            "the geometry is current before anything flushes it");
    }

    public static void Test_StartWithWindows_MirroredIntoTheSavedSettings()
    {
        var harness = OneRule();

        harness.Model.SetStartWithWindows("admin");

        Assert.Equal(1, harness.Saves.Count, "a mode that differs from the file asks for exactly one save");
        Assert.Equal("admin", harness.Saves[^1].Settings.StartWithWindows, "and it carries what Windows reported");
    }

    public static void Test_StartWithWindows_UnchangedModeSavesNothing()
    {
        var harness = new Harness(OneRuleSet(), settings: new Settings { StartWithWindows = "normal" });

        harness.Model.SetStartWithWindows("normal");

        Assert.Equal(0, harness.Saves.Count, "the file already says normal, so no start rewrites it");
    }

    public static void Test_StartWithWindows_LockedGuardWritesNothing()
    {
        var harness = OneRule(guard: WriteGuard.NoPersist);

        harness.Model.SetStartWithWindows("admin");

        Assert.Equal(0, harness.Saves.Count, "a discarded write would only log a save-discarded line per start");
    }

    public static void Test_SaveDebounce_ManyRequestsOneSave()
    {
        var saved = new List<AppConfig>();
        int starts = 0;
        var debounce = new SaveDebounce(saved.Add, () => starts++, () => { });

        for (int i = 0; i < 20; i++) debounce.RequestSave(SampleConfig());

        Assert.Equal(0, saved.Count, "nothing is written before the deadline");
        Assert.Equal(20, starts, "every request pushes the deadline back");

        debounce.OnTimerFired();

        Assert.Equal(1, saved.Count, "twenty clicks collapse into one save");

        debounce.OnTimerFired();
        debounce.FlushNow();

        Assert.Equal(1, saved.Count, "a deadline without a pending config writes nothing");
    }

    public static void Test_SaveDebounce_FlushWritesTheNewestConfig()
    {
        var saved = new List<AppConfig>();
        var debounce = new SaveDebounce(saved.Add, () => { }, () => { });

        debounce.RequestSave(SampleConfig(8));
        debounce.RequestSave(SampleConfig(16));

        Assert.True(debounce.HasPendingSave, "a request is outstanding");

        debounce.FlushNow();

        Assert.Equal(1, saved.Count, "one write");
        Assert.Equal(16, saved[0].Machine.LogicalProcessors, "and it is the newest config, not the first");
        Assert.True(!debounce.HasPendingSave, "nothing is left over");
    }

    public static void Test_Log_SelectionChangedNamesCountAndDescription()
    {
        var harness = OneRule();

        harness.Model.SetSelection(Fixtures.IdA, Fixtures.TwoThreads);

        Assert.True(
            harness.Log.Lines.Contains("Information rules rule 'a.exe' selection changed (2 threads: CCD 0)"),
            "rules.selection-changed carries count and description, word for word");
    }

    public static void Test_Tooltip_ThreadLineCarriesTheDescription()
    {
        var harness = OneRule();

        var lines = harness.Model.Rows[0].Tooltip.Split(Environment.NewLine);

        Assert.True(lines.Contains("4 thr · CCD 0"), "the thread line names the cluster, not just the count");
    }

    public static void Test_Log_RuleEventsUseTheSpecifiedWording()
    {
        var harness = new Harness(RuleSet.Empty);

        harness.Model.AddRule(Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.Machine));
        harness.Model.ToggleSelectedEnabled();
        harness.Model.ToggleSelectedEnabled();
        harness.Model.DeleteSelectedRule();
        harness.Model.DeleteSelectedRule();

        Assert.True(harness.Log.Has(LogLevel.Information, "rules rule 'a.exe' created (all threads, disabled: no)"),
            "rules.created");
        Assert.True(harness.Log.Has(LogLevel.Information, "rules rule 'a.exe' disabled"), "rules.enabled-changed, off");
        Assert.True(harness.Log.Has(LogLevel.Information, "rules rule 'a.exe' enabled"), "rules.enabled-changed, on");
        Assert.True(harness.Log.Has(LogLevel.Information, "rules rule 'a.exe' deleted"), "rules.deleted");
    }

    private static string Names(Harness harness) => string.Join(",", harness.Model.Rows.Select(r => r.ExeName));

    private static RuleRow Row(Harness harness, Guid ruleId) => harness.Model.Rows.First(r => r.RuleId == ruleId);

    private static RuleStatus Status(
        Guid ruleId, RuleState state, int matched = 0, int affected = 0, int firstPid = 0,
        BlockReason reason = BlockReason.None)
        => new(ruleId, state, matched, affected, firstPid, reason);

    private static RuleSet OneRuleSet() => Fixtures.Set(Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf));

    private static Harness OneRule(WriteGuard? guard = null) => new(OneRuleSet(), guard: guard);

    private static Harness TwoRules() => new(Fixtures.Set(
        Fixtures.Rule(Fixtures.IdA, "a.exe", Fixtures.FirstHalf),
        Fixtures.Rule(Fixtures.IdB, "b.exe", Fixtures.FirstHalf)));

    private static AppConfig SampleConfig(int logicalProcessors = 8) => new()
    {
        SchemaVersion = 1,
        Machine = new MachineInfo("Test CPU", logicalProcessors),
        Settings = new Settings(),
        Rules = RuleSet.Empty,
    };

    /// The view model with recording stand-ins for engine, persister and log.
    private sealed class Harness
    {
        internal Harness(
            RuleSet rules,
            WriteGuard? guard = null,
            MachineInfo? loadedMachine = null,
            MachineInfo? measuredMachine = null,
            Settings? settings = null,
            ConfigLoadOutcome outcome = ConfigLoadOutcome.Loaded,
            string? detail = null,
            int rulesSkipped = 0,
            bool isElevated = false)
            => Model = new RuleListViewModel(
                rules,
                guard ?? WriteGuard.Open,
                Fixtures.Machine,
                schemaVersion: 1,
                loadedMachine ?? new MachineInfo("Test CPU", 8),
                measuredMachine ?? new MachineInfo("Test CPU", 8),
                settings ?? new Settings(),
                outcome,
                detail,
                rulesSkipped,
                isElevated,
                (set, change) => Submits.Add((set, change)),
                Saves.Add,
                _ => "CCD 0",
                Log);

        internal List<(RuleSet Rules, RuleChange Change)> Submits { get; } = [];

        internal List<AppConfig> Saves { get; } = [];

        internal RecordingLog Log { get; } = new();

        internal RuleListViewModel Model { get; }
    }
}
