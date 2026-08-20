using CorePin.Core.Layout;
using CorePin.Core.Primitives;
using static CorePin.Tests.LayoutFixtures;

namespace CorePin.Tests;

/// The pure selection operations: toggles, preset application, and the SMT modifier.
public static class SelectionOpsTests
{
    private static AffinityMask Ccd0 => AffinityMask.FromThreads(Enumerable.Range(0, 16));
    private static AffinityMask Ccd0Physical => AffinityMask.FromThreads([0, 2, 4, 6, 8, 10, 12, 14]);

    public static void Test_Preset_ReplacesInsteadOfAdding()
    {
        var topology = Topology(Fixtures.Ryzen7945HX);
        var ccd0Preset = SelectionOps.Presets(topology)[1];
        Assert.Equal("CCD 0", ccd0Preset.Label, "the second preset is CCD 0");

        // A preset click applies this mask as-is — never unioned with whatever was selected before.
        Assert.Equal(SelectionOps.ClusterMask(topology.Clusters[0]), ccd0Preset.Mask, "exactly CCD 0");
        Assert.Equal(16, ccd0Preset.Mask.Count, "sixteen threads, not thirty-two");
        Assert.Equal(false, ccd0Preset.Mask.Contains(16), "nothing of CCD 1 survives");
    }

    public static void Test_All_IsTheMachineMask()
    {
        foreach (var dump in AllDumps)
        {
            var topology = Topology(dump);
            var all = SelectionOps.Presets(topology)[0];
            Assert.Equal("All", all.Label, $"first preset ({dump})");
            Assert.Equal(topology.MachineMask, all.Mask, $"the machine mask itself ({dump})");
        }

        // A machine mask deliberately wider than the cluster union must win as-is.
        var gappy = Synthetic(AffinityMask.FromThreads([0, 1, 42]), Cluster("CCD 0", null, 0, [0, 1]));
        Assert.Equal(gappy.MachineMask, SelectionOps.Presets(gappy)[0].Mask,
            "All reads the machine mask, it does not add up clusters");
    }

    public static void Test_ClusterHeader_TriState()
    {
        var cluster = Topology(Fixtures.Ryzen7945HX).Clusters[0];

        Assert.Equal(Ccd0, SelectionOps.ToggleCluster(AffinityMask.Empty, cluster),
            "empty selects the whole cluster");
        Assert.Equal(Ccd0, SelectionOps.ToggleCluster(AffinityMask.FromThreads([3, 7]), cluster),
            "partial completes first");
        Assert.Equal(AffinityMask.Empty, SelectionOps.ToggleCluster(Ccd0, cluster),
            "complete empties");

        var withOthers = SelectionOps.ToggleCluster(AffinityMask.FromThreads([3, 20]), cluster);
        Assert.Equal(true, withOthers.Contains(20), "threads of other clusters stay untouched");
        Assert.Equal(17, withOthers.Count, "the cluster completes around them");
    }

    public static void Test_HalfCell_TogglesBackAndForth()
    {
        var on = SelectionOps.ToggleThreads(AffinityMask.FromThreads([5]), [8]);
        Assert.Equal(AffinityMask.FromThreads([5, 8]), on, "a slice click adds its thread");

        var off = SelectionOps.ToggleThreads(on, [8]);
        Assert.Equal(AffinityMask.FromThreads([5]), off, "the second click removes it again");
    }

    public static void Test_Seam_TogglesBothThreads()
    {
        var both = SelectionOps.ToggleThreads(AffinityMask.FromThreads([0]), [0, 1]);
        Assert.Equal(AffinityMask.FromThreads([0, 1]), both, "a half-selected cell completes first");

        var none = SelectionOps.ToggleThreads(both, [0, 1]);
        Assert.Equal(AffinityMask.Empty, none, "the next seam click removes both");
    }

    public static void Test_LastThread_NotDeselectable()
    {
        var candidate = SelectionOps.ToggleThreads(AffinityMask.FromThreads([7]), [7]);
        Assert.Equal(true, candidate.IsEmpty,
            "deselecting the last thread yields the empty candidate the control must reject");
    }

    public static void Test_NoEventWithoutChange()
    {
        var topology = Topology(Fixtures.Ryzen7945HX);

        Assert.Equal(Ccd0, SelectionOps.WithSmt(Ccd0, topology),
            "SMT on over a full selection yields an equal candidate — nothing to raise");
        Assert.Equal(Ccd0, SelectionOps.ToggleThreads(SelectionOps.ToggleThreads(Ccd0, [0]), [0]),
            "a double toggle lands on an equal candidate");
    }

    public static void Test_SmtOff_HalvesTheSelection()
    {
        var topology = Topology(Fixtures.Ryzen7945HX);

        Assert.Equal(Ccd0Physical, SelectionOps.WithoutSmt(Ccd0, topology),
            "eight threads remain, each the lower one");
        Assert.Equal(AffinityMask.FromThreads([1]),
            SelectionOps.WithoutSmt(AffinityMask.FromThreads([1]), topology),
            "the lowest selected thread survives, not the lowest of the core");
    }

    public static void Test_SmtOn_AddsTheSiblings()
    {
        var topology = Topology(Fixtures.Ryzen7945HX);

        Assert.Equal(Ccd0, SelectionOps.WithSmt(Ccd0Physical, topology), "sixteen threads again");
        Assert.Equal(AffinityMask.FromThreads([0, 1]),
            SelectionOps.WithSmt(AffinityMask.FromThreads([1]), topology),
            "the sibling of a lone second thread is added");
    }

    public static void Test_SmtOff_CanNeverEmpty()
    {
        foreach (var dump in AllDumps)
        {
            var topology = Topology(dump);
            foreach (int thread in topology.MachineMask.ToThreads())
                Assert.Equal(false,
                    SelectionOps.WithoutSmt(AffinityMask.FromThreads([thread]), topology).IsEmpty,
                    $"a single selected thread survives ({dump}, thread {thread})");
            Assert.Equal(false, SelectionOps.WithoutSmt(topology.MachineMask, topology).IsEmpty,
                $"the full machine survives ({dump})");
            foreach (var cluster in topology.Clusters)
                Assert.Equal(false,
                    SelectionOps.WithoutSmt(SelectionOps.ClusterMask(cluster), topology).IsEmpty,
                    $"a full cluster survives ({dump}, {cluster.Label})");
        }
    }

    public static void Test_SmtPosition_IsDerived()
    {
        var topology = Topology(Fixtures.Ryzen7945HX);

        Assert.Equal(true, SelectionOps.SmtIsOn(AffinityMask.FromThreads([0, 1, 4]), topology),
            "one full core is enough for on");
        Assert.Equal(false, SelectionOps.SmtIsOn(Ccd0Physical, topology),
            "single threads only means off");
        Assert.Equal(false, SelectionOps.SmtIsOn(AffinityMask.Empty, topology), "empty means off");
    }

    public static void Test_SmtToggle_DisabledWhenWithoutEffect()
    {
        var topology = Topology(Fixtures.Core14900K);

        Assert.Equal(false,
            SelectionOps.SmtIsEnabled(SelectionOps.ClusterMask(topology.Clusters[1]), topology),
            "E cores have no second thread, toggling could not change anything");
        Assert.Equal(true,
            SelectionOps.SmtIsEnabled(SelectionOps.ClusterMask(topology.Clusters[0]), topology),
            "P cores can toggle");
        Assert.Equal(false, SelectionOps.SmtIsEnabled(AffinityMask.Empty, topology),
            "an empty selection cannot toggle");
    }

    public static void Test_TwoClicks_Ccd0WithoutSmt()
    {
        var topology = Topology(Fixtures.Ryzen7945HX);

        var afterPreset = SelectionOps.Presets(topology)[1].Mask;
        var afterToggle = SelectionOps.WithoutSmt(afterPreset, topology);

        Assert.Equal(Ccd0Physical, afterToggle, "exactly the eight lower threads of CCD 0");
    }
}
