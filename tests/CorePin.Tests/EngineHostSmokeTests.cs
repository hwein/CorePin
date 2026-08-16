using CorePin.Core.Diagnostics;
using CorePin.Core.Engine;
using CorePin.Core.Primitives;
using CorePin.Core.Rules;
using CorePin.Tests.Fakes;

namespace CorePin.Tests;

/// These drive the worker thread of EngineHost; every deadline comes from FakeClock, never from wall-clock time.
public static class EngineHostSmokeTests
{
    private const int PollIntervalMs = 1000;
    private const int WaitMs = 10000;
    private const int MaxFailures = 10;

    private static readonly AffinityMask Machine = AffinityMask.FromThreads([0, 1, 2, 3, 4, 5, 6, 7]);
    private static readonly AffinityMask FirstHalf = AffinityMask.FromThreads([0, 1, 2, 3]);
    private static readonly AffinityMask SecondHalf = AffinityMask.FromThreads([4, 5, 6, 7]);
    private static readonly AffinityMask TwoThreads = AffinityMask.FromThreads([0, 1]);
    private static readonly DateTime Start = new(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid IdA = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid IdB = new("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid IdMarker = new("cccccccc-0000-0000-0000-000000000003");
    private static readonly RuleSet DisabledRule = Set(Rule(IdA, "a.exe", FirstHalf) with { Enabled = false });

    public static void Test_Smoke1_RemoveSubmittedBeforeStopIsStillExecuted()
    {
        using var f = new HostFixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rules = Set(Rule(IdA, "a.exe", FirstHalf));
        f.Engine.Tick(rules);

        f.Host.Start();
        f.Host.Submit(RuleSet.Empty, new RuleChange(RuleChangeKind.Removed, IdA));
        f.Host.Stop();

        Assert.Equal(Machine, f.Access.CurrentMask(100), "Stop is a queue command, so the release still ran");
        Assert.Equal(0, f.Engine.PinnedProcessCount, "and emptied the pin list");
        Assert.True(f.Log.Has(LogLevel.Information, "watcher stopped"), "the thread ended within its deadline");
    }

    public static void Test_Smoke2_TwentyCommandsLeadToOneApplicationAndNoEnumeration()
    {
        using var f = new HostFixture();
        f.Inventory.Add(100, "a.exe", Start);
        var rules = Set(Rule(IdA, "a.exe", FirstHalf), Marker());

        AwaitHeartbeat(f, f.Host.Start, "the first tick runs immediately");
        int enumerations = f.Inventory.ListCallCount;

        AwaitStatus(f, OnlyRule(IdA), () =>
        {
            for (int i = 0; i < 20; i++)
                f.Host.Submit(rules, new RuleChange(RuleChangeKind.SelectionChanged, IdA));
            AwaitQueueDrained(f, rules);
            f.Clock.Advance(250);
        }, "the debounced application runs once the deadline is due");

        Assert.Equal(1, f.Access.SetCalls.Count, "twenty commands cause exactly one application");
        Assert.Equal(enumerations, f.Inventory.ListCallCount, "and no additional enumeration");
    }

    public static void Test_Smoke3_TenFailuresInARowRaiseFaultedExactlyOnce()
    {
        using var f = new HostFixture();
        f.Inventory.ThrowOnNextList(new InvalidOperationException("inventory unavailable"));

        using var faulted = new ManualResetEventSlim();
        int reported = 0;
        int raised = 0;
        f.Host.Faulted += fault =>
        {
            reported = fault.ConsecutiveFailures;
            raised++;
            faulted.Set();
        };

        f.Host.Start();
        AwaitFailures(f, 1);                          // the tick that runs at once fails on its own
        DriveFailures(f, 2, MaxFailures);

        Assert.True(faulted.Wait(WaitMs), "a permanently failing port makes the loop give up");
        Assert.Equal(1, raised, "Faulted is raised exactly once");
        Assert.Equal(MaxFailures, reported, "after ten consecutive failures");
        Assert.Equal(MaxFailures, f.Log.Count("pass failed"), "every failed pass is logged");
        Assert.True(f.Log.Has(LogLevel.Critical, "watcher gave up after 10 consecutive failures, monitoring stopped"),
            "the application no longer does what it exists for, so the last line is critical");
    }

    public static void Test_ASuccessfulPassResetsTheFailureCounter()
    {
        using var f = new HostFixture();
        f.Inventory.ThrowOnNextList(new InvalidOperationException("inventory unavailable"));

        int raised = 0;
        f.Host.Faulted += _ => raised++;

        f.Host.Start();
        AwaitFailures(f, 1);
        DriveFailures(f, 2, MaxFailures - 1);

        f.Inventory.ThrowOnNextList(null);
        AwaitHeartbeat(f, () =>
        {
            f.Clock.Advance(PollIntervalMs);
            f.Host.Submit(DisabledRule, new RuleChange(RuleChangeKind.None, Guid.Empty));
        }, "the recovered port lets one pass complete");

        f.Inventory.ThrowOnNextList(new InvalidOperationException("inventory unavailable again"));
        DriveFailures(f, MaxFailures, 2 * (MaxFailures - 1));

        Assert.Equal(0, raised, "nine failures, one good pass and nine more are never ten in a row");
        Assert.Equal(2, f.Log.Count("pass failed (1)"), "the counter started over at the good pass");
        Assert.True(!f.Log.Has(LogLevel.Critical, "watcher gave up"), "so the watcher never gave up");
    }

    public static void Test_NoStoppedLineAfterTheWatcherGaveUp()
    {
        using var f = new HostFixture();
        f.Inventory.ThrowOnNextList(new InvalidOperationException("inventory unavailable"));

        using var faulted = new ManualResetEventSlim();
        f.Host.Faulted += _ => faulted.Set();

        f.Host.Start();
        AwaitFailures(f, 1);
        DriveFailures(f, 2, MaxFailures);
        Assert.True(faulted.Wait(WaitMs), "the loop gives up");

        f.Host.Stop();

        Assert.True(!f.Log.Has(LogLevel.Information, "watcher stopped"),
            "a watcher that gave up is not stopped in an orderly way");
        Assert.True(!f.Log.Has(LogLevel.Warning, "did not stop within"), "and its thread had ended all the same");
    }

    public static void Test_Smoke4_AllThreadsDuringARunningDeadlineReleasesWithoutPinningAgain()
    {
        using var f = new HostFixture();
        f.Inventory.Add(100, "a.exe", Start);
        var restricted = Set(Rule(IdA, "a.exe", FirstHalf), Marker());

        AwaitHeartbeat(f, f.Host.Start, "the first tick runs immediately");
        AwaitHeartbeat(f, () =>
        {
            f.Host.Submit(restricted, new RuleChange(RuleChangeKind.None, Guid.Empty));
            AwaitQueueDrained(f, restricted);
            f.Clock.Advance(PollIntervalMs);
        }, "the next tick pins the process");
        Assert.Equal(1, f.Access.SetCalls.Count, "the rule is applied once");

        var allThreads = Set(Rule(IdA, "a.exe", Machine), Marker());
        AwaitStatus(f, s => s.Count == 1 && s[0].RuleId == IdA && s[0].State == RuleState.NoRestriction, () =>
        {
            f.Host.Submit(Set(Rule(IdA, "a.exe", TwoThreads), Marker()),
                          new RuleChange(RuleChangeKind.SelectionChanged, IdA));
            f.Host.Submit(allThreads, new RuleChange(RuleChangeKind.SelectionChanged, IdA));
        }, "selecting all threads releases and reports no restriction");

        AwaitHeartbeat(f, () => f.Clock.Advance(PollIntervalMs), "a later tick runs");

        Assert.Equal(2, f.Access.SetCalls.Count, "one pin and one release — the pending deadline never fired");
        Assert.Equal(Machine, f.Access.CurrentMask(100), "the process stays released");
    }

    public static void Test_Smoke5_DeadlinesAreKeptPerRule()
    {
        using var f = new HostFixture();
        f.Inventory.Add(100, "a.exe", Start);
        f.Inventory.Add(101, "b.exe", Start);
        var rules = Set(Rule(IdA, "a.exe", FirstHalf), Rule(IdB, "b.exe", SecondHalf), Marker());

        AwaitHeartbeat(f, f.Host.Start, "the first tick runs immediately");

        f.Host.Submit(rules, new RuleChange(RuleChangeKind.SelectionChanged, IdA));
        AwaitQueueDrained(f, rules);
        f.Clock.Advance(100);
        f.Host.Submit(rules, new RuleChange(RuleChangeKind.SelectionChanged, IdB));
        AwaitQueueDrained(f, rules);

        AwaitStatus(f, OnlyRule(IdA), () => f.Clock.Advance(150),
            "the deadline of rule A survived the command for rule B");
        Assert.Equal(1, f.Access.SetCalls.Count, "and rule B is still waiting for its own deadline");

        AwaitStatus(f, OnlyRule(IdB), () => f.Clock.Advance(100), "rule B applies on its own deadline");

        Assert.Equal(2, f.Access.SetCalls.Count, "each rule applied exactly once");
        Assert.Equal(FirstHalf, f.Access.CurrentMask(100), "rule A hit its process");
        Assert.Equal(SecondHalf, f.Access.CurrentMask(101), "rule B hit its own");
    }

    private static Func<IReadOnlyList<RuleStatus>, bool> OnlyRule(Guid ruleId)
        => statuses => statuses.Count == 1 && statuses[0].RuleId == ruleId;

    /// A failed pass moves the next tick away, so each further failure needs a command that throws in ApplyRule.
    private static void DriveFailures(HostFixture f, int from, int to)
    {
        for (int n = from; n <= to; n++)
        {
            f.Host.Submit(DisabledRule, new RuleChange(RuleChangeKind.EnabledChanged, IdA));
            AwaitFailures(f, n);
        }
    }

    private static void AwaitFailures(HostFixture f, int count)
    {
        long deadline = Environment.TickCount64 + WaitMs;
        while (f.Log.Count("pass failed") < count)
        {
            Assert.True(Environment.TickCount64 < deadline, $"the loop reports {count} failed passes");
            Thread.Sleep(1);
        }
    }

    /// A single-rule status list can only come from ApplyRule, never from a tick.
    private static void AwaitQueueDrained(HostFixture f, RuleSet rules)
        => AwaitStatus(f, OnlyRule(IdMarker),
            () => f.Host.Submit(rules, new RuleChange(RuleChangeKind.EnabledChanged, IdMarker)),
            "the worker processed every command submitted so far");

    private static void AwaitStatus(HostFixture f, Func<IReadOnlyList<RuleStatus>, bool> predicate,
                                    Action trigger, string because)
    {
        using var seen = new ManualResetEventSlim();
        void OnStatus(IReadOnlyList<RuleStatus> statuses)
        {
            if (predicate(statuses)) seen.Set();
        }

        f.Host.StatusChanged += OnStatus;
        try
        {
            trigger();
            Assert.True(seen.Wait(WaitMs), because);
        }
        finally { f.Host.StatusChanged -= OnStatus; }
    }

    private static void AwaitHeartbeat(HostFixture f, Action trigger, string because)
    {
        using var seen = new ManualResetEventSlim();
        void OnHeartbeat(EngineHeartbeat heartbeat) => seen.Set();

        f.Host.Heartbeat += OnHeartbeat;
        try
        {
            trigger();
            Assert.True(seen.Wait(WaitMs), because);
        }
        finally { f.Host.Heartbeat -= OnHeartbeat; }
    }

    private static Rule Marker() => Rule(IdMarker, "marker.exe", FirstHalf) with { Enabled = false };

    private static Rule Rule(Guid id, string exeName, AffinityMask threads)
        => new() { Id = id, ExeName = exeName, Threads = threads };

    private static RuleSet Set(params Rule[] rules) => new(rules);

    private sealed class HostFixture : IDisposable
    {
        internal HostFixture()
        {
            Access = new FakeAffinityAccess(Inventory, Machine);
            Engine = new AffinityEngine(Inventory, Access, Clock, Log, Machine);
            Host = new EngineHost(Engine, Clock, Log, PollIntervalMs);
        }

        internal FakeProcessInventory Inventory { get; } = new();
        internal FakeAffinityAccess Access { get; }
        internal FakeClock Clock { get; } = new();
        internal RecordingLog Log { get; } = new();
        internal AffinityEngine Engine { get; }
        internal EngineHost Host { get; }

        public void Dispose() => Host.Dispose();
    }
}
