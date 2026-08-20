using CorePin.Core.Layout;
using CorePin.Core.Primitives;
using static CorePin.Tests.LayoutFixtures;

namespace CorePin.Tests;

/// One mask, one sentence: the four description cases in their fixed order.
public static class SelectionDescriptionTests
{
    public static void Test_Describe()
    {
        var hx = Topology(Fixtures.Ryzen7945HX);
        var ccd0 = AffinityMask.FromThreads(Enumerable.Range(0, 16));
        var physical0 = AffinityMask.FromThreads([0, 2, 4, 6, 8, 10, 12, 14]);
        var physicalBoth = AffinityMask.FromThreads(Enumerable.Range(0, 16).Select(i => i * 2));

        Assert.Equal("all cores", SelectionDescription.Describe(hx, hx.MachineMask), "machine mask");
        Assert.Equal("CCD 0", SelectionDescription.Describe(hx, ccd0), "one full cluster");
        Assert.Equal("CCD 0 (physical)", SelectionDescription.Describe(hx, physical0),
            "the lowest thread of every core");
        Assert.Equal("CCD 0 + CCD 1 (physical)", SelectionDescription.Describe(hx, physicalBoth),
            "physical across clusters, suffix once");
        Assert.Equal("custom", SelectionDescription.Describe(hx, AffinityMask.FromThreads([0, 16])),
            "neither clusters nor physical sets");
        Assert.Equal("custom", SelectionDescription.Describe(hx, AffinityMask.FromThreads([1])),
            "a lone second thread");
        Assert.Equal("custom", SelectionDescription.Describe(hx, AffinityMask.Empty), "empty");
        Assert.Equal("custom", SelectionDescription.Describe(hx, AffinityMask.FromThreads(
            Enumerable.Range(0, 16).Concat(Enumerable.Range(0, 8).Select(i => 16 + i * 2)))),
            "a full cluster plus a physical-only cluster never mixes into a combination");

        var mixed = Topology(Fixtures.CoreUltra155H);
        Assert.Equal("P-Cores + E-Cores",
            SelectionDescription.Describe(mixed, AffinityMask.FromThreads(Enumerable.Range(0, 20))),
            "two of three clusters, joined in topology order");

        var threeCcds = Synthetic(
            Cluster("CCD 0", null, 0, [0, 1]),
            Cluster("CCD 1", null, 0, [2, 3]),
            Cluster("CCD 2", null, 0, [4, 5]));
        Assert.Equal("CCD 0 + CCD 1",
            SelectionDescription.Describe(threeCcds, AffinityMask.FromThreads([0, 1, 2, 3])),
            "a cluster union below the machine mask");

        var k = Topology(Fixtures.Core14900K);
        Assert.Equal("E-Cores",
            SelectionDescription.Describe(k, AffinityMask.FromThreads(Enumerable.Range(16, 16))),
            "a full single-thread cluster is the cluster, not (physical)");
    }
}
