using CorePin.Core.Engine;
using CorePin.Core.Rules;
using CorePin.Core.ViewModel;

namespace CorePin.Tests;

/// The pure text functions behind a rule row and the status line — no view model needed.
public static class RuleListTextTests
{
    public static void Test_LineBudget_CutsInTheMiddle()
    {
        Assert.Equal(30, LineBudget.MaxChars, "the second line budget is 30 characters");
        Assert.Equal("short", LineBudget.Fit("short"), "text within the budget stays untouched");

        string cut = LineBudget.Fit(new string('a', 20) + new string('b', 20));

        Assert.Equal(30, cut.Length, "the cut result fills the budget exactly");
        Assert.Equal("aaaaaaaaaaaaaaa…bbbbbbbbbbbbbb", cut, "head, ellipsis, tail");
    }

    public static void Test_LineBudget_RejectsAnImpossibleBudget()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => LineBudget.Fit("anything", 0), "a budget below one character has no result");

    /// 24 is the budget RuleRow.DisplayName uses for line 1, narrower than the second line's 30.
    public static void Test_LineBudget_NarrowerBudgetForDisplayName()
    {
        string longName = new string('a', 15) + new string('b', 15);
        string cut = LineBudget.Fit(longName, 24);

        Assert.Equal(24, cut.Length, "a narrower budget still cuts to exactly that width");
        Assert.Equal(new string('a', 12) + "…" + new string('b', 11), cut,
            "head, ellipsis, tail at the narrower budget — the ending stays visible");
        Assert.Equal("short.exe", LineBudget.Fit("short.exe", 24),
            "a name within the narrower budget stays untouched");
    }

    public static void Test_SecondLine_TextOfEveryState()
    {
        Assert.Equal("not running", RuleRowText.SecondLine(Status(RuleState.Idle)), "Idle");
        Assert.Equal("no restriction", RuleRowText.SecondLine(Status(RuleState.NoRestriction, 1)), "NoRestriction");
        Assert.Equal("disabled", RuleRowText.SecondLine(Status(RuleState.Disabled)), "Disabled");
        Assert.Equal("Needs review", RuleRowText.SecondLine(Status(RuleState.Invalid)), "Invalid");
        Assert.Equal("pinned · PID 18244",
            RuleRowText.SecondLine(Status(RuleState.Applied, 1, 1, 18244)), "one process names its PID");
        Assert.Equal("pinned · 3 processes",
            RuleRowText.SecondLine(Status(RuleState.Applied, 3, 3, 18244)), "several processes are counted");
    }

    public static void Test_SecondLine_SymbolPerState()
    {
        Assert.Equal('●', RuleRowText.Symbol(RuleState.Applied), "Applied");
        Assert.Equal('◐', RuleRowText.Symbol(RuleState.Blocked), "Blocked");
        Assert.Equal('◐', RuleRowText.Symbol(RuleState.Invalid), "Invalid");
        Assert.Equal('○', RuleRowText.Symbol(RuleState.Idle), "Idle");
        Assert.Equal('○', RuleRowText.Symbol(RuleState.NoRestriction), "NoRestriction");
        Assert.Equal('○', RuleRowText.Symbol(RuleState.Disabled), "Disabled");
    }

    public static void Test_Count_MatchedMinusAffected()
    {
        Assert.Equal("Needs admin rights · 1 of 3",
            RuleRowText.SecondLine(Status(RuleState.Blocked, 3, 2, 1, BlockReason.NeedsAdminRights)),
            "the count names the processes in the worse state, not the pinned ones");
        Assert.Equal("Blocked by Windows",
            RuleRowText.SecondLine(Status(RuleState.Blocked, 1, 0, 1, BlockReason.BlockedByWindows)),
            "a single process needs no count");
    }

    public static void Test_Delete_ConfirmationTextWithinLineBudget()
    {
        Assert.Equal(26, RuleRowText.DeleteConfirmation.Length, "the confirmation text is 26 characters");
        Assert.Equal("Delete? Del again · Esc no", LineBudget.Fit(RuleRowText.DeleteConfirmation),
            "the confirmation text passes the budget uncut");

        const string reference = "Needs admin rights · 1 of 2";
        Assert.Equal(27, reference.Length, "the reference line is 27 characters");
        Assert.Equal(reference, LineBudget.Fit(reference), "the reference line passes the budget uncut");
    }

    public static void Test_StatusLine_ThreeFormsAndTheFourth()
    {
        Assert.Equal("Watching for apps — no rules yet", StatusLine.Compose(0, null, null), "no rules");
        Assert.Equal("Watching 3 rules", StatusLine.Compose(3, null, null), "rules, none applied");
        Assert.Equal("Watching 3 rules · applied handbrake.exe 3 s ago",
            StatusLine.Compose(3, "handbrake.exe", TimeSpan.FromSeconds(3)), "at least one applied");
        Assert.Equal("Stopped watching — quit from the tray icon and start CorePin again",
            StatusLine.Stopped, "the fourth form after a fault");
    }

    public static void Test_StatusLine_AgoIsStaged()
    {
        Assert.Equal("0 s ago", StatusLine.Ago(TimeSpan.Zero), "zero");
        Assert.Equal("59 s ago", StatusLine.Ago(TimeSpan.FromSeconds(59)), "below a minute");
        Assert.Equal("1 min ago", StatusLine.Ago(TimeSpan.FromSeconds(60)), "a minute");
        Assert.Equal("59 min ago", StatusLine.Ago(TimeSpan.FromMinutes(59)), "below an hour");
        Assert.Equal("1 h ago", StatusLine.Ago(TimeSpan.FromHours(1)), "an hour");
        Assert.Equal("3 h ago", StatusLine.Ago(TimeSpan.FromHours(3.9)), "above an hour, rounded down");
        Assert.Equal("0 s ago", StatusLine.Ago(TimeSpan.FromSeconds(-5)), "a clock jump never shows a negative age");
    }

    private static RuleStatus Status(
        RuleState state, int matched = 0, int affected = 0, int firstPid = 0,
        BlockReason reason = BlockReason.None)
        => new(Fixtures.IdA, state, matched, affected, firstPid, reason);
}
