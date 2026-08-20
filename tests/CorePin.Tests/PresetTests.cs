using System.Globalization;
using CorePin.Core.Layout;
using CorePin.Core.Primitives;
using static CorePin.Tests.LayoutFixtures;

namespace CorePin.Tests;

/// Preset derivation from the topology and the header line the card prints per cluster.
public static class PresetTests
{
    public static void Test_PresetsFromTopology()
    {
        Assert.Equal("All,CCD 0,CCD 1", Labels(Fixtures.Ryzen7945HX), "7945HX");
        Assert.Equal("All,P-Cores,E-Cores", Labels(Fixtures.Core14900K), "14900K");
        Assert.Equal("All,P-Cores,E-Cores,LP E-Cores", Labels(Fixtures.CoreUltra155H), "155H");
        Assert.Equal("All,Group 0,Group 1", Labels(Fixtures.Phoenix2), "Phoenix 2");
    }

    public static void Test_PresetLabelWithoutBadge()
    {
        var topology = Synthetic(Cluster("CCD 0", "V-Cache", 100_663_296,
            [0, 1], [2, 3], [4, 5], [6, 7], [8, 9], [10, 11], [12, 13], [14, 15]));

        var presets = SelectionOps.Presets(topology);
        Assert.Equal("All,CCD 0", string.Join(",", presets.Select(p => p.Label)),
            "the badge stays in the header, not on the preset");
    }

    public static void Test_PresetMaskIsClusterMask()
    {
        foreach (var dump in AllDumps)
        {
            var topology = Topology(dump);
            var presets = SelectionOps.Presets(topology);
            Assert.Equal(topology.Clusters.Count + 1, presets.Count, $"All plus one per cluster ({dump})");
            for (int i = 0; i < topology.Clusters.Count; i++)
            {
                var expected = AffinityMask.FromThreads(
                    topology.Clusters[i].Cores.SelectMany(c => c.Threads));
                Assert.Equal(expected, presets[i + 1].Mask,
                    $"OR over all threads of all cores ({dump}, preset {i + 1})");
            }
        }
    }

    public static void Test_HeaderText()
    {
        var ccd0 = Build(Fixtures.Ryzen7945HX, 400).Clusters[0];
        Assert.Equal("CCD 0", ccd0.HeaderLabel, "label");
        Assert.Equal("8C/16T · 32 MB L3", ccd0.HeaderFacts, "facts");

        var eCluster = Build(Fixtures.Core14900K, 400).Clusters[1];
        Assert.Equal("16C/16T · 36 MB L3", eCluster.HeaderFacts, "the shared L3 shows its full size");

        var lpE = Build(Fixtures.CoreUltra155H, 400).Clusters[2];
        Assert.Equal("LP E-Cores", lpE.HeaderLabel, "label without L3");
        Assert.Equal("2C/2T", lpE.HeaderFacts, "no L3 part at all");

        var vCache = CardLayoutBuilder.Build(
            Synthetic(Cluster("CCD 0", "V-Cache", 100_663_296,
                [0, 1], [2, 3], [4, 5], [6, 7], [8, 9], [10, 11], [12, 13], [14, 15])),
            400, Metrics()).Clusters[0];
        Assert.Equal("V-Cache · 8C/16T · 96 MB L3", vCache.HeaderFacts, "the badge leads the facts");
    }

    public static void Test_L3Formatting()
    {
        Assert.Equal("8C/16T · 32 MB L3", FactsFor(33_554_432), "32 MB exactly");
        Assert.Equal("8C/16T · 96 MB L3", FactsFor(100_663_296), "96 MB exactly");

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("8C/16T · 32.5 MB L3", FactsFor(34_078_720), "one decimal, invariant culture");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static string Labels(string dump)
        => string.Join(",", SelectionOps.Presets(Topology(dump)).Select(p => p.Label));

    private static string FactsFor(long l3Bytes)
    {
        var topology = Synthetic(Cluster("CCD 0", null, l3Bytes,
            [0, 1], [2, 3], [4, 5], [6, 7], [8, 9], [10, 11], [12, 13], [14, 15]));
        return CardLayoutBuilder.Build(topology, 400, Metrics()).Clusters[0].HeaderFacts;
    }
}
