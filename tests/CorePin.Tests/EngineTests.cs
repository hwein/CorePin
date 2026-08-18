using CorePin.Core.Diagnostics;
using CorePin.Core.Engine;
using CorePin.Core.Platform;
using CorePin.Core.Rules;
using CorePin.Tests.Fakes;
using static CorePin.Tests.Fixtures;

namespace CorePin.Tests;

/// No thread, no sleep, no real process.
public static class EngineTests
{
    private const string TransitionLine = "Information engine rule 'a.exe':";

    public static void Test_T01_DeleteIsNotLostAgainstARunningTick()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rules = Set(Rule(IdA, "a.exe", FirstHalf));

        f.Engine.Tick(rules);
        f.Engine.Release(IdA, ReleaseReason.Removed);
        f.Engine.Tick(RuleSet.Empty);

        Assert.Equal(2, f.Access.SetCalls.Count, "one pin and one reset, no third set");
        Assert.Equal(Machine, f.Access.CurrentMask(100), "the process ends on its system mask");
        Assert.Equal(0, f.Engine.PinnedProcessCount, "the pin list is empty afterwards");
    }

    public static void Test_T02_RuleCatchesAnAlreadyRunningProcess()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "notepad.exe", Start);

        var status = f.Engine.Tick(Set(Rule(IdA, "notepad.exe", FirstHalf)))[0];

        Assert.Equal(RuleState.Applied, status.State, "the first tick pins a process that was already running");
        Assert.Equal(FirstHalf, f.Access.CurrentMask(100), "the process carries the rule mask");

        var withoutExtension = f.Engine.Tick(Set(Rule(IdB, "notepad", FirstHalf)))[0];

        Assert.Equal(RuleState.Idle, withoutExtension.State,
            "the name comparison runs on the file name WITH extension");
    }

    public static void Test_T03_MatchingMaskStillEntersThePinList()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Access.SetProcessMask(100, FirstHalf);

        var status = f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf)))[0];

        Assert.Equal(0, f.Access.SetCalls.Count, "nothing had to be set");
        Assert.Equal(RuleState.Applied, status.State, "a process already on the target mask counts as handled");
        Assert.Equal(1, f.Engine.PinnedProcessCount, "the PID enters the pin list anyway");

        f.Engine.Release(IdA, ReleaseReason.Disabled);

        Assert.Equal(Machine, f.Access.CurrentMask(100), "and the later release finds it");
    }

    public static void Test_T04_ReapplyAfterAForeignMaskChange()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rules = Set(Rule(IdA, "a.exe", FirstHalf));
        f.Engine.Tick(rules);

        f.Access.SetProcessMask(100, Machine);
        f.Engine.Tick(rules);

        Assert.Equal(2, f.Access.SetCalls.Count, "the repeated comparison sets the mask again");
        Assert.Equal(FirstHalf, f.Access.CurrentMask(100), "the rule mask is restored");
    }

    public static void Test_T05_OneOfThreeProcessesCannotBeOpened()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Inventory.Add(101, "a.exe", Start);
        f.Inventory.Add(102, "a.exe", Start);
        f.Access.FailOpen(101, OpenFailure.AccessDenied, 5);

        var status = f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf)))[0];

        Assert.Equal(RuleState.Blocked, status.State, "the worse state wins");
        Assert.Equal(BlockReason.NeedsAdminRights, status.Reason, "the open path means missing rights");
        Assert.Equal(3, status.MatchedProcesses, "the blocked process still counts as a match");
        Assert.Equal(2, status.AffectedProcesses, "two of three were handled");
        Assert.True(f.Log.Has(LogLevel.Debug, "OpenProcess PID 101 failed, Win32 5 ACCESS_DENIED"),
            "the Win32 code goes to the log");
    }

    public static void Test_T06_SetAffinityFailsOnOneOfTwoProcesses()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Inventory.Add(101, "a.exe", Start);
        f.Access.FailSet(101, 87);

        var status = f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf)))[0];

        Assert.Equal(RuleState.Blocked, status.State, "a failed set blocks the rule");
        Assert.Equal(BlockReason.BlockedByWindows, status.Reason, "the affinity path means Windows refused");
        Assert.Equal(2, status.MatchedProcesses, "both processes match");
        Assert.Equal(1, status.AffectedProcesses, "one of two was handled");
        Assert.True(f.Log.Has(LogLevel.Debug, "SetAffinity PID 101 failed, Win32 87 INVALID_PARAMETER"),
            "the Win32 code goes to the log");
    }

    public static void Test_T07_NarrowProcessSystemMaskLeavesTheRuleValid()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Access.SetSystemMask(100, FirstHalf);

        var status = f.Engine.Tick(Set(Rule(IdA, "a.exe", SecondHalf)))[0];

        Assert.Equal(RuleState.Blocked, status.State, "only the set fails");
        Assert.Equal(BlockReason.BlockedByWindows, status.Reason, "the set is the place of failure");
    }

    public static void Test_T08_ReleaseUsesTheProcessSystemMask()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Access.SetSystemMask(100, FirstHalf);
        f.Engine.Tick(Set(Rule(IdA, "a.exe", TwoThreads)));

        f.Engine.Release(IdA, ReleaseReason.Disabled);

        Assert.Equal(FirstHalf, f.Access.CurrentMask(100), "the reset goes to the process system mask");
        Assert.True(f.Access.SetCalls[^1].Mask != Machine, "and not to the machine mask");
        Assert.True(f.Log.Has(LogLevel.Information, "rule 'a.exe': released PID 100 (disabled)"),
            "the release line names the reason");
    }

    public static void Test_T09_RecycledPidIsNotTouchedOnRelease()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf)));

        f.Access.RecycleAsNewProcess(100, Start.AddSeconds(5));
        f.Engine.Release(IdA, ReleaseReason.Disabled);

        Assert.Equal(1, f.Access.SetCalls.Count, "the foreign process is not touched");
        Assert.Equal(0, f.Engine.PinnedProcessCount, "the entry is dropped all the same");
        Assert.True(!f.Log.HasAtOrAbove(LogLevel.Warning),
            "a recycled PID is not a failure, so no failure-class line is written at all");
        Assert.True(f.Log.Has(LogLevel.Debug, "PID 100 start time mismatch, treated as different process"),
            "it is logged as what it is");
    }

    public static void Test_T10_ReleaseWorksAfterARestartOfCorePin()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Access.SetProcessMask(100, FirstHalf);                 // pinned in a previous session

        f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf)));
        f.Engine.Release(IdA, ReleaseReason.Removed);

        Assert.Equal(1, f.Access.SetCalls.Count, "the only set call is the reset");
        Assert.Equal(Machine, f.Access.CurrentMask(100), "the process is released after a restart");
    }

    public static void Test_T11_NeedsAdminRightsWinsOverBlockedByWindows()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Inventory.Add(101, "a.exe", Start);
        f.Inventory.Add(102, "a.exe", Start);
        f.Access.FailOpen(100, OpenFailure.AccessDenied, 5);
        f.Access.FailSet(101, 87);

        var status = f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf)))[0];

        Assert.Equal(BlockReason.NeedsAdminRights, status.Reason, "the actionable reason wins");
        Assert.Equal(3, status.MatchedProcesses, "all three match");
        Assert.Equal(1, status.AffectedProcesses, "one of three was handled");
    }

    public static void Test_T12_NeedsReviewBeatsACoincidentallyFittingMask()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rule = Rule(IdA, "a.exe", Machine) with { NeedsReview = true };

        var status = f.Engine.Tick(Set(rule))[0];

        Assert.Equal(RuleState.Invalid, status.State, "after a CPU change every rule is Invalid");
        Assert.Equal(0, f.Access.SetCalls.Count, "an invalid rule touches no process");
    }

    public static void Test_T13_AllThreadsSelectedReleasesAndReportsNoRestriction()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf)));

        var allThreads = Set(Rule(IdA, "a.exe", Machine));
        f.Engine.Release(IdA, ReleaseReason.AllThreadsSelected);
        var status = f.Engine.ApplyRule(allThreads, IdA)[0];

        Assert.Equal(RuleState.NoRestriction, status.State, "a running process with all threads is no restriction");
        Assert.Equal(2, f.Access.SetCalls.Count, "pin and reset, no second pin");
        Assert.True(f.Log.Has(LogLevel.Information, "released PID 100 (all threads selected)"),
            "the release line names the reason");

        f.Inventory.Remove(100);
        var idle = f.Engine.Tick(allThreads)[0];

        Assert.Equal(RuleState.Idle, idle.State, "without a running process the same rule is Idle");
    }

    public static void Test_T14_NameChangedBetweenEnumerationAndOpen()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Access.SetHandleName(100, "other.exe");

        var status = f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf)))[0];

        Assert.Equal(RuleState.Idle, status.State, "a reused PID is no match");
        Assert.Equal(0, status.MatchedProcesses, "and does not count as one");
        Assert.Equal(0, f.Access.SetCalls.Count, "the foreign process is not pinned");
        Assert.True(f.Log.Has(LogLevel.Debug,
            "PID 100: name changed since enumeration ('a.exe' -> 'other.exe'), handle discarded"),
            "the discarded handle is logged");
    }

    public static void Test_T15_CheckOrderDisabledBeforeNoRestrictionBeforeInvalid()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);

        var disabled = f.Engine.Tick(Set(Rule(IdA, "a.exe", Machine) with { Enabled = false }))[0];
        Assert.Equal(RuleState.Disabled, disabled.State, "disabled wins over every mask property");

        var all = f.Engine.Tick(Set(Rule(IdA, "a.exe", Machine)))[0];
        Assert.Equal(RuleState.NoRestriction, all.State, "a fitting full selection is no restriction, not Invalid");
    }

    public static void Test_T16_GoneIsNoErrorBranch()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Inventory.Add(101, "a.exe", Start);
        f.Access.FailOpen(101, OpenFailure.Gone, 87);
        var rules = Set(Rule(IdA, "a.exe", FirstHalf));

        var status = f.Engine.Tick(rules)[0];

        Assert.Equal(RuleState.Applied, status.State, "a process that ended is not a blocked one");
        Assert.Equal(1, status.MatchedProcesses, "and does not count as a match");

        f.Access.FailOpen(100, OpenFailure.Gone, 87);
        var idle = f.Engine.Tick(rules)[0];

        Assert.Equal(RuleState.Idle, idle.State, "with every match gone the rule is Idle, not Blocked");
    }

    public static void Test_T17_MissingStartTimeIsAnErrorBranch()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Access.FailStartTime(100, 6);

        var status = f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf)))[0];

        Assert.Equal(RuleState.Blocked, status.State, "an unconfirmed identity is not silently skipped");
        Assert.Equal(BlockReason.BlockedByWindows, status.Reason, "the place of failure is the affinity access");
        Assert.Equal(1, status.MatchedProcesses, "the process counts as a match");
        Assert.Equal(0, f.Engine.PinnedProcessCount, "without a start time there is no pin entry");
    }

    public static void Test_T18_TickPrunesTheEntryOfAVanishedProcess()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rules = Set(Rule(IdA, "a.exe", FirstHalf));
        f.Engine.Tick(rules);
        Assert.Equal(1, f.Engine.PinnedProcessCount, "the process is pinned");

        f.Inventory.Remove(100);
        f.Engine.Tick(rules);

        Assert.Equal(0, f.Engine.PinnedProcessCount, "the next tick drops the entry");
    }

    public static void Test_T19_ApplyRuleWithoutAPreviousTickEnumeratesOnce()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);

        var status = f.Engine.ApplyRule(Set(Rule(IdA, "a.exe", FirstHalf)), IdA)[0];

        Assert.Equal(1, f.Inventory.ListCallCount, "exactly one enumeration");
        Assert.Equal(RuleState.Applied, status.State, "the rule takes effect all the same");
    }

    public static void Test_T20_ApplyRuleForAMissingRuleDoesNothing()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);

        var statuses = f.Engine.ApplyRule(Set(Rule(IdB, "a.exe", FirstHalf)), IdA);

        Assert.Equal(0, statuses.Count, "an unknown rule id yields an empty list");
        Assert.Equal(0, f.Inventory.ListCallCount, "and touches no port");
        Assert.Equal(0, f.Access.OpenCallCount, "neither the enumeration nor the affinity access");
        Assert.Equal(0, f.Access.SetCalls.Count, "and no process");
    }

    public static void Test_T21_FailedReleaseStaysVisibleWhileTheProcessRuns()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rule = Rule(IdA, "a.exe", FirstHalf);
        f.Engine.Tick(Set(rule));

        f.Access.FailSet(100, 87);
        f.Engine.Release(IdA, ReleaseReason.Disabled);

        var disabled = Set(rule with { Enabled = false });
        var afterRelease = f.Engine.ApplyRule(disabled, IdA)[0];

        Assert.Equal(RuleState.Blocked, afterRelease.State, "a failed release outranks Disabled");
        Assert.Equal(BlockReason.BlockedByWindows, afterRelease.Reason, "Windows refused the change");
        Assert.Equal(1, afterRelease.MatchedProcesses - afterRelease.AffectedProcesses, "one of one is still pinned");
        Assert.True(f.Log.Has(LogLevel.Error,
            "rule 'a.exe': failed to release PID 100, Win32 87 INVALID_PARAMETER — process remains pinned"),
            "the failed release is an error");

        Assert.Equal(RuleState.Blocked, f.Engine.Tick(disabled)[0].State, "a tick leaves that standing");

        f.Inventory.Remove(100);
        Assert.Equal(RuleState.Disabled, f.Engine.Tick(disabled)[0].State,
            "once the process is gone the rule falls back");
    }

    public static void Test_T22_FailedReleaseOnRemovedIsLoggedButNotRemembered()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rule = Rule(IdA, "a.exe", FirstHalf);
        f.Engine.Tick(Set(rule));

        f.Access.FailSet(100, 87);
        f.Engine.Release(IdA, ReleaseReason.Removed);

        Assert.Equal(0, f.Engine.PinnedProcessCount, "the entry is dropped, there is no later trigger");
        Assert.Equal(1, f.Log.Count("failed to release PID 100"), "exactly one warning");

        var status = f.Engine.ApplyRule(Set(rule with { Enabled = false }), IdA)[0];

        Assert.Equal(RuleState.Disabled, status.State, "nothing was written to the failed-release memory");
    }

    public static void Test_T23_ReleaseFailsIdenticallyOnOpenStartTimeAndMaskRead()
    {
        AssertReleaseFailure(f => f.Access.FailOpen(100, OpenFailure.AccessDenied, 5), 5, "ACCESS_DENIED");
        AssertReleaseFailure(f => f.Access.FailStartTime(100, 6), 6, "INVALID_HANDLE");
        AssertReleaseFailure(f => f.Access.FailGetAffinity(100, 87), 87, "INVALID_PARAMETER");
    }

    public static void Test_T24_OneFailedReleaseOfThreeIsCountedAsOneOfThree()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Inventory.Add(101, "a.exe", Start);
        f.Inventory.Add(102, "a.exe", Start);
        var rule = Rule(IdA, "a.exe", FirstHalf);
        f.Engine.Tick(Set(rule));

        f.Access.FailSet(101, 87);
        f.Engine.Release(IdA, ReleaseReason.Disabled);

        var status = f.Engine.ApplyRule(Set(rule with { Enabled = false }), IdA)[0];

        Assert.Equal(3, status.MatchedProcesses, "all three processes still run");
        Assert.Equal(2, status.AffectedProcesses, "two were released, one stayed pinned");
    }

    public static void Test_T25_TheFailedReleaseStateDoesNotStickToANewProcess()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rule = Rule(IdA, "a.exe", FirstHalf);
        f.Engine.Tick(Set(rule));

        f.Access.FailSet(100, 87);
        f.Engine.Release(IdA, ReleaseReason.Disabled);

        f.Inventory.Remove(100);
        f.Inventory.Add(200, "a.exe", Start);
        var status = f.Engine.Tick(Set(rule with { Enabled = false }))[0];

        Assert.Equal(RuleState.Disabled, status.State,
            "a newly started process of the same name does not keep the rule amber");
    }

    public static void Test_EngineState_IsClearedOnRuleRemoval()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rule = Rule(IdA, "a.exe", FirstHalf);
        var disabled = Set(rule with { Enabled = false });
        f.Engine.Tick(Set(rule));
        f.Access.FailSet(100, 87);
        f.Engine.Release(IdA, ReleaseReason.Disabled);

        f.Engine.Release(IdA, ReleaseReason.Removed);

        Assert.Equal(RuleState.Disabled, f.Engine.ApplyRule(disabled, IdA)[0].State,
            "Removed drops the failed-release memory");
        Assert.True(f.Log.Has(LogLevel.Information, "rule 'a.exe': Idle -> Disabled"),
            "and the logged state with it: the next observation is a first sighting, not Applied -> Disabled");

        var g = new Fixture();
        g.Inventory.Add(100, "a.exe", Start);
        g.Engine.Tick(Set(rule));
        g.Access.FailSet(100, 87);
        g.Engine.Release(IdA, ReleaseReason.Disabled);

        g.Engine.Tick(RuleSet.Empty);

        Assert.Equal(RuleState.Disabled, g.Engine.ApplyRule(disabled, IdA)[0].State,
            "a rule that vanishes without a command is caught by the tick");
        Assert.True(g.Log.Has(LogLevel.Information, "rule 'a.exe': Idle -> Disabled"),
            "including its logged state");
    }

    public static void Test_ASecondReleaseOverAnEmptyPinListKeepsTheFailedOneVisible()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rule = Rule(IdA, "a.exe", FirstHalf);
        f.Engine.Tick(Set(rule));

        f.Access.FailSet(100, 87);
        f.Engine.Release(IdA, ReleaseReason.Disabled);
        Assert.Equal(0, f.Engine.PinnedProcessCount, "the failed entry left the pin list");

        var reselected = Set(rule with { Enabled = false, Threads = SecondHalf });
        f.Engine.Release(IdA, ReleaseReason.Disabled);       // the core selection changed while the rule is off
        var status = f.Engine.ApplyRule(reselected, IdA)[0];

        Assert.Equal(RuleState.Blocked, status.State, "a release that visited nothing releases nothing");
        Assert.Equal(BlockReason.BlockedByWindows, status.Reason, "PID 100 is still pinned");
        Assert.Equal(1, status.MatchedProcesses - status.AffectedProcesses, "and is still counted");
    }

    public static void Test_T27_TransitionIsLoggedOncePerChangeAndInCountFormat()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Inventory.Add(101, "a.exe", Start);
        f.Access.FailSet(101, 87);
        var rules = Set(Rule(IdA, "a.exe", FirstHalf));

        f.Engine.Tick(rules);
        f.Engine.Tick(rules);
        f.Engine.Tick(rules);

        Assert.Equal(1, f.Log.Count(TransitionLine), "an unchanged rule writes one line over three ticks");

        f.Inventory.Add(102, "a.exe", Start);
        f.Engine.Tick(rules);

        Assert.Equal(2, f.Log.Count(TransitionLine), "a changed count writes a second line");
        Assert.True(f.Log.Has(LogLevel.Information, "rule 'a.exe': Blocked (1 of 2 -> 2 of 3)"),
            "a pure count change uses the count format instead of Blocked -> Blocked");
    }

    public static void Test_Hysteresis_IsTimeBasedNotCallBased()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rules = Set(Rule(IdA, "a.exe", FirstHalf));
        f.Engine.Tick(rules);

        f.Inventory.Remove(100);
        f.Engine.Tick(rules);
        int before = f.Log.Count(TransitionLine);

        for (int i = 0; i < 20; i++) f.Engine.ApplyRule(rules, IdA);

        Assert.Equal(before, f.Log.Count(TransitionLine), "twenty calls inside the window write no Idle line");

        f.Clock.Advance(3000);
        f.Engine.ApplyRule(rules, IdA);

        Assert.Equal(before + 1, f.Log.Count(TransitionLine), "past IdleHysteresisMs the Idle line appears");
        Assert.True(f.Log.Has(LogLevel.Information, "rule 'a.exe': Applied -> Idle"), "and names the transition");
    }

    public static void Test_T28_ProcessingOrderIsAscendingByPid()
    {
        var f = new Fixture();
        foreach (int pid in new[] { 500, 400, 300, 200, 100 }) f.Inventory.Add(pid, "a.exe", Start);

        var status = f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf)))[0];

        Assert.Equal(100, status.FirstPid, "FirstPid is the smallest matching PID");
        Assert.Equal("100,200,300,400,500", string.Join(",", f.Access.SetCalls.Select(c => c.Pid)),
            "the processes are handled in ascending PID order");
    }

    public static void Test_T29_DuplicateExeNameLetsTheFirstRuleWin()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);

        var statuses = f.Engine.Tick(Set(Rule(IdA, "a.exe", FirstHalf), Rule(IdB, "a.exe", SecondHalf)));

        Assert.Equal(RuleState.Applied, statuses[0].State, "the first rule in the set wins");
        Assert.Equal(RuleState.Idle, statuses[1].State, "the second one sees no process");
        Assert.Equal(FirstHalf, f.Access.CurrentMask(100), "and the mask of the first rule is applied");
        Assert.True(f.Log.Has(LogLevel.Debug, "duplicate exeName ignored"), "the defensive branch is logged");
    }

    public static void Test_T30_ARuleWithoutHitsGetsAnEmptyMatchList()
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rules = Set(Rule(IdA, "a.exe", FirstHalf), Rule(IdB, "ghost.exe", FirstHalf));

        var statuses = f.Engine.Tick(rules);

        Assert.Equal(RuleState.Idle, statuses[1].State, "a rule without a hit is Idle, not an exception");
        Assert.Equal(0, statuses[1].MatchedProcesses, "and reports zero matches");
        Assert.Equal(RuleState.Idle, f.Engine.ApplyRule(rules, IdB)[0].State, "the same holds for ApplyRule");
    }

    private static void AssertReleaseFailure(Action<Fixture> arm, int win32Error, string name)
    {
        var f = new Fixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rule = Rule(IdA, "a.exe", FirstHalf);
        f.Engine.Tick(Set(rule));

        arm(f);
        f.Engine.Release(IdA, ReleaseReason.Disabled);

        Assert.True(f.Log.Has(LogLevel.Error,
            $"failed to release PID 100, Win32 {win32Error} {name} — process remains pinned"),
            $"a release failing at Win32 {win32Error} is treated like a failing set");
        Assert.Equal(0, f.Engine.PinnedProcessCount, "the entry is dropped in every failure case");
        Assert.Equal(RuleState.Blocked, f.Engine.ApplyRule(Set(rule with { Enabled = false }), IdA)[0].State,
            "and the rule stays visibly blocked");
    }

    private sealed class Fixture
    {
        internal Fixture()
        {
            Access = new FakeAffinityAccess(Inventory, Machine);
            Engine = new AffinityEngine(Inventory, Access, Clock, Log, Machine);
        }

        internal FakeProcessInventory Inventory { get; } = new();
        internal FakeAffinityAccess Access { get; }
        internal FakeClock Clock { get; } = new();
        internal RecordingLog Log { get; } = new();
        internal AffinityEngine Engine { get; }
    }
}
