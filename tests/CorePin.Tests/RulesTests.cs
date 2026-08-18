using CorePin.Core.Primitives;
using CorePin.Core.Rules;

namespace CorePin.Tests;

/// RuleSet basics and RuleEvaluation's binding check order; engine cases live in EngineTests.
public static class RulesTests
{
    private static readonly AffinityMask Machine = AffinityMask.FromThreads([0, 1, 2, 3]);

    public static void Test_RuleSet_MarkAllForReview_MarksEveryRule()
    {
        var set = new RuleSet([Rule("a.exe", 0), Rule("b.exe", 1)]);

        var marked = set.MarkAllForReview();

        Assert.True(set.Rules.All(r => !r.NeedsReview), "the original set stays unmarked");
        Assert.True(marked.Rules.All(r => r.NeedsReview), "every rule of the new set is marked");
        Assert.Equal(2, marked.Rules.Count, "no rule is lost");
    }

    public static void Test_RuleSet_WithReplacesSameIdAndWithoutRemoves()
    {
        var first = Rule("a.exe", 0);
        var set = new RuleSet([first, Rule("b.exe", 1)]);

        var replaced = set.With(first with { ExeName = "renamed.exe" });
        Assert.Equal(2, replaced.Rules.Count, "an existing id is replaced, not appended");
        Assert.Equal("renamed.exe", replaced.Rules[0].ExeName, "the replacement keeps its position");

        var added = set.With(Rule("c.exe", 2));
        Assert.Equal(3, added.Rules.Count, "an unknown id is appended");

        Assert.Equal(1, set.Without(first.Id).Rules.Count, "Without removes by id");
        Assert.True(set.ById(first.Id) is not null, "ById finds a known rule");
    }

    public static void Test_RuleSet_IsImmutableAgainstItsSource()
    {
        var source = new List<Rule> { Rule("a.exe", 0) };
        var set = new RuleSet(source);

        source.Add(Rule("b.exe", 1));

        Assert.Equal(1, set.Rules.Count, "RuleSet copies on construction");
    }

    public static void Test_RuleEvaluation_FollowsTheBindingOrder()
    {
        Assert.Equal(RuleCheck.Disabled,
            RuleEvaluation.Check(Rule("a.exe", 0) with { Enabled = false, NeedsReview = true }, Machine),
            "1. !Enabled wins over everything");

        Assert.Equal(RuleCheck.Invalid,
            RuleEvaluation.Check(Rule("a.exe", 0, 1, 2, 3) with { NeedsReview = true }, Machine),
            "2. NeedsReview stands BEFORE NoRestriction — after a CPU change every rule is Invalid");

        Assert.Equal(RuleCheck.NoRestriction,
            RuleEvaluation.Check(Rule("a.exe", 0, 1, 2, 3), Machine),
            "3. a selection equal to the machine mask is no restriction");

        Assert.Equal(RuleCheck.Invalid,
            RuleEvaluation.Check(Rule("a.exe", 0, 9), Machine),
            "4. a mask that does not fit the machine is Invalid");

        Assert.Equal(RuleCheck.Apply,
            RuleEvaluation.Check(Rule("a.exe", 0, 1), Machine),
            "5. everything else is applied");
    }

    private static Rule Rule(string exeName, params int[] threads) => new()
    {
        Id = Guid.Parse($"{threads[0]:D8}-0000-0000-0000-000000000000"),
        ExeName = exeName,
        Threads = AffinityMask.FromThreads(threads),
    };
}
