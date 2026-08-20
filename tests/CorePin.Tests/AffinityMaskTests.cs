using CorePin.Core.Primitives;

namespace CorePin.Tests;

/// Direct coverage of the mask primitives the card operations are built on.
public static class AffinityMaskTests
{
    public static void Test_Contains_EdgeBits()
    {
        var mask = AffinityMask.FromThreads([0, 63]);
        Assert.Equal(true, mask.Contains(0), "bit 0");
        Assert.Equal(true, mask.Contains(63), "bit 63");
        Assert.Equal(false, mask.Contains(1), "an unset bit");
        Assert.Equal(false, mask.Contains(-1), "below the range is false, not a throw");
        Assert.Equal(false, mask.Contains(64), "above the range is false, not a throw");
        Assert.Equal(false, AffinityMask.Empty.Contains(0), "empty contains nothing");
    }

    public static void Test_With_SetsOneBit()
    {
        Assert.Equal(1UL, AffinityMask.Empty.With(0).Value, "bit 0");
        Assert.Equal(1UL << 63, AffinityMask.Empty.With(63).Value, "bit 63");

        var mask = AffinityMask.FromThreads([5]);
        Assert.Equal(mask, mask.With(5), "setting a set bit changes nothing");
        Assert.Throws<ArgumentOutOfRangeException>(() => AffinityMask.Empty.With(64), "64 is out of range");
    }

    public static void Test_Without_ClearsOneBit()
    {
        var mask = AffinityMask.FromThreads([0, 63]);
        Assert.Equal(AffinityMask.FromThreads([63]), mask.Without(0), "clears bit 0");
        Assert.Equal(AffinityMask.FromThreads([0]), mask.Without(63), "clears bit 63");
        Assert.Equal(mask, mask.Without(7), "clearing an unset bit changes nothing");
        Assert.Equal(true, AffinityMask.FromThreads([4]).Without(4).IsEmpty, "the last bit may go");
        Assert.Throws<ArgumentOutOfRangeException>(() => mask.Without(-1), "-1 is out of range");
    }

    public static void Test_FromThreads_CollectsBits()
    {
        Assert.Equal(AffinityMask.Empty, AffinityMask.FromThreads([]), "no threads, empty mask");
        Assert.Equal((1UL << 63) | 1UL, AffinityMask.FromThreads([63, 0]).Value, "order does not matter");
        Assert.Equal(AffinityMask.FromThreads([4]), AffinityMask.FromThreads([4, 4, 4]), "duplicates collapse");
        Assert.Equal(ulong.MaxValue, AffinityMask.FromThreads(Enumerable.Range(0, 64)).Value, "the full machine");
        Assert.Equal(64, AffinityMask.FromThreads(Enumerable.Range(0, 64)).Count, "count of the full machine");
        Assert.Throws<ArgumentOutOfRangeException>(() => AffinityMask.FromThreads([64]), "64 is out of range");
    }

    public static void Test_ToThreads_ListsAscending()
    {
        Assert.Equal(0, AffinityMask.Empty.ToThreads().Count, "empty lists nothing");
        Assert.Equal("0,5,63", string.Join(",", AffinityMask.FromThreads([63, 5, 0]).ToThreads()),
            "ascending regardless of input order");
        Assert.Equal(string.Join(",", Enumerable.Range(0, 64)),
            string.Join(",", AffinityMask.FromThreads(Enumerable.Range(0, 64)).ToThreads()),
            "all 64 bits round-trip");
    }

    public static void Test_FitsInto_IsTheSubsetCheck()
    {
        var subset = AffinityMask.FromThreads([0, 63]);
        var superset = AffinityMask.FromThreads([0, 5, 63]);
        Assert.Equal(true, subset.FitsInto(superset), "a subset fits");
        Assert.Equal(false, superset.FitsInto(subset), "a superset does not");
        Assert.Equal(true, subset.FitsInto(subset), "every mask fits into itself");
        Assert.Equal(true, AffinityMask.Empty.FitsInto(AffinityMask.Empty), "empty fits into empty");
        Assert.Equal(true, AffinityMask.Empty.FitsInto(subset), "empty fits into anything");
        Assert.Equal(false, subset.FitsInto(AffinityMask.Empty), "nothing non-empty fits into empty");
    }
}
