using CorePin.Core.Topology;
using static CorePin.Tests.Fixtures;

namespace CorePin.Tests;

/// The four frozen dumps, determinism and purity, and the error and edge cases.
public static class ClusterBuilderTests
{
    public static void Test_Ryzen7945HX_TwoCcdsSameClass()
    {
        var snapshot = Load(Ryzen7945HX);
        var topology = ClusterBuilder.Build(snapshot);

        Assert.Equal("AuthenticAMD", topology.Vendor, "vendor");
        Assert.Equal(32, topology.LogicalProcessorCount, "logical processors");
        Assert.Equal("0x00000000FFFFFFFF", topology.MachineMask.ToHex(), "machine mask");
        Assert.Equal(true, topology.HasSmt, "SMT everywhere");
        Assert.Equal(ProfilingLevel.Profiled, topology.Profiling, "profiled");
        Assert.Equal("", string.Join(";", topology.Notes), "no notes");
        Assert.Equal(2, topology.Clusters.Count, "two CCDs — one efficiency class, two L3 groups");

        AssertCluster(topology.Clusters[0], "CCD 0", null, 8, 16, 33_554_432, true);
        AssertCluster(topology.Clusters[1], "CCD 1", null, 8, 16, 33_554_432, true);
        Assert.Equal(string.Join(",", Enumerable.Range(0, 16)), ThreadsOf(topology.Clusters[0]), "CCD 0 threads 0…15");
        Assert.Equal(string.Join(",", Enumerable.Range(16, 16)), ThreadsOf(topology.Clusters[1]), "CCD 1 threads 16…31");
        Assert.Equal("0,1", string.Join(",", topology.Clusters[0].Cores[0].Threads), "core 0 of CCD 0");
        Assert.Equal("16,17", string.Join(",", topology.Clusters[1].Cores[0].Threads), "core 0 of CCD 1");

        AssertStructuralInvariants(topology, snapshot);
    }

    public static void Test_Core14900K_EfficiencyClassSplitsOneL3()
    {
        var snapshot = Load(Core14900K);
        var topology = ClusterBuilder.Build(snapshot);

        Assert.Equal(32, topology.LogicalProcessorCount, "logical processors");
        Assert.Equal("0x00000000FFFFFFFF", topology.MachineMask.ToHex(), "machine mask");
        Assert.Equal(true, topology.HasSmt, "the P-cores have HT");
        Assert.Equal(ProfilingLevel.Profiled, topology.Profiling, "profiled");
        Assert.Equal("", string.Join(";", topology.Notes), "no notes");
        Assert.Equal(2, topology.Clusters.Count, "exactly two clusters despite ONE shared L3");

        // Both clusters show the same 36 MB — they share the cache.
        AssertCluster(topology.Clusters[0], "P-Cores", null, 8, 16, 37_748_736, true);
        AssertCluster(topology.Clusters[1], "E-Cores", null, 16, 16, 37_748_736, true);
        Assert.Equal(string.Join(",", Enumerable.Range(0, 16)), ThreadsOf(topology.Clusters[0]), "P-core threads 0…15");
        Assert.Equal(string.Join(",", Enumerable.Range(16, 16)), ThreadsOf(topology.Clusters[1]), "E-core threads 16…31");

        AssertStructuralInvariants(topology, snapshot);
    }

    public static void Test_CoreUltra155H_LpECoresWithoutL3()
    {
        var snapshot = Load(CoreUltra155H);
        var topology = ClusterBuilder.Build(snapshot);

        Assert.Equal(22, topology.LogicalProcessorCount, "logical processors");
        Assert.Equal("0x00000000003FFFFF", topology.MachineMask.ToHex(), "machine mask");
        Assert.Equal(true, topology.HasSmt, "the P-cores have HT");
        Assert.Equal(ProfilingLevel.Profiled, topology.Profiling, "profiled");
        Assert.Equal("", string.Join(";", topology.Notes), "no notes");
        Assert.Equal(3, topology.Clusters.Count, "three clusters");

        AssertCluster(topology.Clusters[0], "P-Cores", null, 6, 12, 25_165_824, true);
        AssertCluster(topology.Clusters[1], "E-Cores", null, 8, 8, 25_165_824, true);
        AssertCluster(topology.Clusters[2], "LP E-Cores", null, 2, 2, 0, false);
        Assert.Equal(string.Join(",", Enumerable.Range(0, 12)), ThreadsOf(topology.Clusters[0]), "P-core threads 0…11");
        Assert.Equal(string.Join(",", Enumerable.Range(12, 8)), ThreadsOf(topology.Clusters[1]), "E-core threads 12…19");
        Assert.Equal("20,21", ThreadsOf(topology.Clusters[2]), "the LP-E cores are the last two threads");

        // The separation comes from the L3 alone: every core of clusters 1 and 2 carries
        // efficiencyClass 0 in the input snapshot (formulated over the snapshot because
        // CpuCluster carries no EfficiencyClass).
        var classZero = snapshot.Cores.Where(c => c.EfficiencyClass == 0)
                                      .Aggregate(0UL, (acc, c) => acc | c.Mask);
        Assert.Equal(MaskOf(topology.Clusters[1]) | MaskOf(topology.Clusters[2]), classZero,
            "E-Cores and LP E-Cores both carry efficiencyClass 0");

        AssertStructuralInvariants(topology, snapshot);
    }

    public static void Test_Phoenix2_FallsBackToGroups()
    {
        var snapshot = Load(Phoenix2);
        var topology = ClusterBuilder.Build(snapshot);

        Assert.Equal(12, topology.LogicalProcessorCount, "logical processors");
        Assert.Equal("0x0000000000000FFF", topology.MachineMask.ToHex(), "machine mask");
        Assert.Equal(true, topology.HasSmt, "SMT on both core kinds");
        Assert.Equal(ProfilingLevel.NotProfiled, topology.Profiling, "AMD with two efficiency classes");
        Assert.Equal("", string.Join(";", topology.Notes), "no notes");
        Assert.Equal(2, topology.Clusters.Count, "two clusters");

        // No badge in either: 16777216 >= 8 MiB x 6 = 50331648 is false. The wrong reading
        // (per cluster) would give the two-core cluster exactly 8.0 MB.
        AssertCluster(topology.Clusters[0], "Group 0", null, 2, 4, 16_777_216, true);
        AssertCluster(topology.Clusters[1], "Group 1", null, 4, 8, 16_777_216, true);
        Assert.Equal(string.Join(",", Enumerable.Range(0, 4)), ThreadsOf(topology.Clusters[0]), "Group 0 threads 0…3");
        Assert.Equal(string.Join(",", Enumerable.Range(4, 8)), ThreadsOf(topology.Clusters[1]), "Group 1 threads 4…11");

        AssertStructuralInvariants(topology, snapshot);
    }

    /// The assumption-is-wrong outcome, built in code from the loaded dump instead of a fifth file.
    public static void Test_Phoenix2_SingleClassGivesOneCcd()
    {
        var loaded = Load(Phoenix2);
        var snapshot = loaded with
        {
            Cores = [.. loaded.Cores.Select(c => c with { EfficiencyClass = 0 })],
        };

        var topology = ClusterBuilder.Build(snapshot);

        Assert.Equal(ProfilingLevel.Profiled, topology.Profiling, "one class, one L3 group ⇒ profiled");
        Assert.Equal("", string.Join(";", topology.Notes), "no notes");
        Assert.Equal(1, topology.Clusters.Count, "one cluster instead of two");
        AssertCluster(topology.Clusters[0], "CCD 0", null, 6, 12, 16_777_216, true);
    }

    public static void Test_SameResultTwice()
    {
        var snapshot = Load(Ryzen7945HX);
        Assert.Equal(Describe(ClusterBuilder.Build(snapshot)), Describe(ClusterBuilder.Build(snapshot)),
            "Build twice on the same snapshot gives the same result");
    }

    public static void Test_CoreOrderDoesNotMatter()
    {
        var snapshot = Load(Ryzen7945HX);
        var reversed = snapshot with { Cores = [.. snapshot.Cores.Reverse()] };

        Assert.Equal(Describe(ClusterBuilder.Build(snapshot)), Describe(ClusterBuilder.Build(reversed)),
            "Build sorts explicitly and does not rely on the input order");
    }

    public static void Test_CacheOrderDoesNotMatter()
    {
        var snapshot = Load(Ryzen7945HX);
        var reversed = snapshot with { Caches = [.. snapshot.Caches.Reverse()] };

        Assert.Equal(Describe(ClusterBuilder.Build(snapshot)), Describe(ClusterBuilder.Build(reversed)),
            "the cache order does not change the result");
    }

    public static void Test_ConstructedAndCapturedByHaveNoEffect()
    {
        var snapshot = Load(Ryzen7945HX);
        var other = snapshot with { Constructed = true, CapturedBy = "CorePin 9.9.9" };

        Assert.Equal(Describe(ClusterBuilder.Build(snapshot)), Describe(ClusterBuilder.Build(other)),
            "capturedBy and constructed must not influence the result");
    }

    /// No model table, not even by accident.
    public static void Test_CpuNameHasNoEffect()
    {
        var snapshot = Load(Ryzen7945HX);
        Assert.Equal(Describe(ClusterBuilder.Build(snapshot)),
                     Describe(ClusterBuilder.Build(snapshot with { CpuName = "" })),
                     "an empty cpuName changes nothing");
        Assert.Equal(Describe(ClusterBuilder.Build(snapshot)),
                     Describe(ClusterBuilder.Build(snapshot with { CpuName = "Fantasy 9000X3D" })),
                     "an invented cpuName changes nothing");
    }

    /// The sharpest test of the labels-versus-structure separation.
    public static void Test_VendorOnlyAffectsLabels()
    {
        var amd = ClusterBuilder.Build(Load(Ryzen7945HX));
        var intel = ClusterBuilder.Build(Load(Ryzen7945HX) with { Vendor = "GenuineIntel" });

        Assert.Equal(amd.Clusters.Count, intel.Clusters.Count, "same number of clusters");
        Assert.Equal(amd.MachineMask.ToHex(), intel.MachineMask.ToHex(), "same machine mask");
        for (int i = 0; i < amd.Clusters.Count; i++)
            Assert.Equal(ThreadsOf(amd.Clusters[i]), ThreadsOf(intel.Clusters[i]), $"cluster {i} has the same cores");

        Assert.Equal("CCD 0,CCD 1", string.Join(",", amd.Clusters.Select(c => c.Label)), "AMD labels");
        Assert.Equal("Group 0,Group 1", string.Join(",", intel.Clusters.Select(c => c.Label)),
            "Intel with one class but two clusters falls back");
    }

    public static void Test_MoreThanOneGroupIsRejected()
        => Assert.Throws<TopologyFormatException>(
            () => ClusterBuilder.Build(Snapshot("AuthenticAMD", [MakeCore(0x3)], activeGroupCount: 2)),
            "activeGroupCount 2 is rejected — the masks would be group-relative");

    public static void Test_ZeroCoresIsRejected()
        => Assert.Throws<TopologyFormatException>(
            () => ClusterBuilder.Build(Snapshot("AuthenticAMD", [])),
            "an empty core list is rejected");

    public static void Test_OverlappingCoreMasksAreRejected()
        => Assert.Throws<TopologyFormatException>(
            () => ClusterBuilder.Build(Snapshot("AuthenticAMD", [MakeCore(0x3), MakeCore(0x1)])),
            "a logical processor cannot belong to two physical cores");

    public static void Test_EmptyCoreMaskIsRejected()
        => Assert.Throws<TopologyFormatException>(
            () => ClusterBuilder.Build(Snapshot("AuthenticAMD", [MakeCore(0x0)])),
            "a core without logical processors does not exist");

    public static void Test_WithoutL3Records()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("AuthenticAMD", [MakeCore(0x1), MakeCore(0x2), MakeCore(0x4), MakeCore(0x8)]));

        Assert.Equal(1, topology.Clusters.Count, "one cluster");
        Assert.Equal(false, topology.Clusters[0].HasL3, "no L3");
        Assert.Equal(0L, topology.Clusters[0].L3Bytes, "L3Bytes 0");
        Assert.Equal("Group 0", topology.Clusters[0].Label, "generic label");
        Assert.Equal(ProfilingLevel.NotProfiled, topology.Profiling, "AMD core without L3 group");
    }

    public static void Test_UnknownVendor()
    {
        var topology = ClusterBuilder.Build(Load(Ryzen7945HX) with { Vendor = "CentaurHauls" });

        Assert.Equal(ProfilingLevel.NotProfiled, topology.Profiling, "unknown vendor is never profiled");
        Assert.Equal("Group 0,Group 1", string.Join(",", topology.Clusters.Select(c => c.Label)), "generic labels");
        Assert.Equal(2, topology.Clusters.Count, "the cluster building is unchanged");
    }

    public static void Test_EmptyVendor()
    {
        var topology = ClusterBuilder.Build(Load(Ryzen7945HX) with { Vendor = "" });

        Assert.Equal(ProfilingLevel.NotProfiled, topology.Profiling, "an empty vendor is never profiled");
        Assert.Equal("Group 0,Group 1", string.Join(",", topology.Clusters.Select(c => c.Label)), "generic labels");
    }

    public static void Test_CoresWithoutSmt()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("AuthenticAMD", [MakeCore(0x1), MakeCore(0x2), MakeCore(0x4), MakeCore(0x8)],
                     [MakeCache(0xF, 8_388_608)]));

        Assert.Equal(false, topology.HasSmt, "no SMT");
        Assert.Equal(true, topology.Clusters[0].Cores.All(c => c.Threads.Count == 1),
            "every physical core has exactly one thread");
    }

    public static void Test_SmtFlagContradictsMask()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("AuthenticAMD", [MakeCore(0x3, smt: false)], [MakeCache(0x3, 8_388_608)]));

        Assert.Equal(2, topology.Clusters[0].Cores[0].Threads.Count, "the mask decides, not the flag");
        Assert.Equal(true, topology.HasSmt, "HasSmt follows the mask");
        Assert.Equal("core: smt flag contradicts mask 0x0000000000000003",
            string.Join(";", topology.Notes), "note set, nothing thrown");
    }

    public static void Test_OverlappingL3Masks()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("AuthenticAMD", [MakeCore(0x3), MakeCore(0xC), MakeCore(0x30)],
                     [MakeCache(0x0F, 8_388_608), MakeCache(0x3C, 8_388_608)]));

        Assert.Equal(ProfilingLevel.NotProfiled, topology.Profiling, "overlapping L3 masks");
        Assert.Equal("l3: overlapping masks 0x000000000000000F 0x000000000000003C",
            string.Join(";", topology.Notes), "note set");
        Assert.Equal(0x3FUL, topology.MachineMask.Value, "every core is assigned");
        Assert.Equal(3, topology.Clusters.Sum(c => c.PhysicalCoreCount), "no core is lost");
    }

    /// A harmless note: it changes no assignment, so the level stays untouched.
    public static void Test_L3CoversUnknownProcessors()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("AuthenticAMD", [MakeCore(0x3), MakeCore(0xC)], [MakeCache(0xFF, 8_388_608)]));

        Assert.Equal(ProfilingLevel.Profiled, topology.Profiling, "harmless note, still profiled");
        Assert.Equal("l3: mask covers unknown processors 0x00000000000000FF",
            string.Join(";", topology.Notes), "note set");
        Assert.Equal("CCD 0", topology.Clusters[0].Label, "label unchanged");
    }

    /// The duplicate-mask merge and the profiling decision together — both must say Profiled.
    public static void Test_DuplicateL3RecordStaysProfiled()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("AuthenticAMD", [MakeCore(0x3), MakeCore(0xC)],
                     [MakeCache(0xF, 8_388_608), MakeCache(0xF, 16_777_216)]));

        Assert.Equal(1, topology.Clusters.Count, "one cluster, not two");
        Assert.Equal("CCD 0", topology.Clusters[0].Label, "CCD 0");
        Assert.Equal(ProfilingLevel.Profiled, topology.Profiling, "the merge is exactly right, nothing is guessed");
        Assert.Equal(16_777_216L, topology.Clusters[0].L3Bytes, "sizeBytes is the maximum");
        Assert.Equal("l3: duplicate mask 0x000000000000000F", string.Join(";", topology.Notes), "note set");
    }

    public static void Test_L3GroupWithoutCoresIsDropped()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("AuthenticAMD", [MakeCore(0x3), MakeCore(0xC), MakeCore(0x30), MakeCore(0xC0)],
                     [MakeCache(0x0F, 8_388_608), MakeCache(0xF0, 8_388_608), MakeCache(0xF00, 8_388_608)]));

        Assert.Equal(2, topology.Clusters.Count, "the core-less group produces no empty cluster");
        Assert.Equal(true, topology.Clusters.All(c => c.PhysicalCoreCount > 0), "no empty cluster");
        // NotProfiled, so the labels are generic — the numbering runs without a gap.
        Assert.Equal(ProfilingLevel.NotProfiled, topology.Profiling, "l3 group without cores");
        Assert.Equal("Group 0,Group 1", string.Join(",", topology.Clusters.Select(c => c.Label)),
            "numbering without a gap");
        Assert.Equal("l3: mask covers unknown processors 0x0000000000000F00;l3: group without cores 0x0000000000000F00",
            string.Join(";", topology.Notes), "both notes, in creation-step order");
    }

    /// Cache mask 0x…0F01 sorts first by lowest set bit, but its cores start at thread 8.
    public static void Test_CcdNumberingFollowsCoreIndex()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("AuthenticAMD", [MakeCore(0x300), MakeCore(0xC00), MakeCore(0x30), MakeCore(0xC0)],
                     [MakeCache(0xF01, 8_388_608), MakeCache(0xF0, 8_388_608)]));

        Assert.Equal(ProfilingLevel.Profiled, topology.Profiling, "the unknown-processor note is harmless");
        Assert.Equal(2, topology.Clusters.Count, "two clusters");
        Assert.Equal("CCD 0", topology.Clusters[0].Label, "CCD 0 holds the lowest thread index");
        Assert.Equal("4,5,6,7", ThreadsOf(topology.Clusters[0]), "CCD 0 is the group starting at thread 4");
        Assert.Equal("CCD 1", topology.Clusters[1].Label, "CCD 1");
        Assert.Equal("8,9,10,11", ThreadsOf(topology.Clusters[1]), "CCD 1 is the group starting at thread 8");
    }

    public static void Test_PartiallyCoveredCore()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("AuthenticAMD", [MakeCore(0x3), MakeCore(0xC)], [MakeCache(0xD, 8_388_608)]));

        Assert.Equal(1, topology.Clusters.Count, "one cluster");
        Assert.Equal(2, topology.Clusters[0].PhysicalCoreCount, "the half-attached core is assigned");
        Assert.Equal(ProfilingLevel.NotProfiled, topology.Profiling, "partial core coverage");
        Assert.Equal("l3: partial core coverage 0x0000000000000003",
            string.Join(";", topology.Notes), "note set");
    }

    /// The structure-note downgrade also applies on the Intel labeling path.
    public static void Test_IntelCaseA_WithStructureNote()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("GenuineIntel", [MakeCore(0x3), MakeCore(0xC)], [MakeCache(0xD, 8_388_608)]));

        Assert.Equal(1, topology.Clusters.Count, "one cluster with L3");
        Assert.Equal(true, topology.Clusters[0].HasL3, "the cluster has an L3");
        Assert.Equal(ProfilingLevel.NotProfiled, topology.Profiling, "the structure note downgrades here too");
        Assert.Equal("Group 0", topology.Clusters[0].Label, "generic label");
    }

    public static void Test_IntelThirdClassCollides()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("GenuineIntel",
                     [MakeCore(0x3, efficiencyClass: 2), MakeCore(0xC, efficiencyClass: 1), MakeCore(0x30, efficiencyClass: 0)],
                     [MakeCache(0x3F, 25_165_824)]));

        Assert.Equal(3, topology.Clusters.Count, "three clusters");
        Assert.Equal(ProfilingLevel.NotProfiled, topology.Profiling, "two clusters would both be E-Cores");
        Assert.Equal("Group 0,Group 1,Group 2", string.Join(",", topology.Clusters.Select(c => c.Label)),
            "generic labels");
    }

    public static void Test_GapInMachineMask()
    {
        var topology = ClusterBuilder.Build(
            Snapshot("AuthenticAMD", [MakeCore(0x1), MakeCore(0x2), MakeCore(0x10), MakeCore(0x20)]));

        Assert.Equal(0x33UL, topology.MachineMask.Value, "gaps in the machine mask are allowed");
        Assert.Equal(4, topology.LogicalProcessorCount, "PopCount, not the highest index");

        ulong presets = 0;
        foreach (var cluster in topology.Clusters) presets |= MaskOf(cluster);
        Assert.Equal(topology.MachineMask.Value, presets, "the presets cover exactly the machine mask");
    }

    private static void AssertCluster(
        CpuCluster cluster, string label, string? badge, int cores, int threads, long l3Bytes, bool hasL3)
    {
        Assert.Equal(label, cluster.Label, "cluster label");
        Assert.Equal(badge, cluster.Badge, $"{label}: badge");
        Assert.Equal(cores, cluster.PhysicalCoreCount, $"{label}: physical cores");
        Assert.Equal(threads, cluster.LogicalCount, $"{label}: logical processors");
        Assert.Equal(l3Bytes, cluster.L3Bytes, $"{label}: L3 bytes");
        Assert.Equal(hasL3, cluster.HasL3, $"{label}: has L3");
    }

    private static void AssertStructuralInvariants(CpuTopology topology, TopologySnapshot snapshot)
    {
        Assert.Equal(true, topology.Clusters.All(c => c.PhysicalCoreCount > 0), "no cluster is empty");

        var threads = topology.Clusters.SelectMany(c => c.Cores).SelectMany(c => c.Threads).ToList();
        Assert.Equal(threads.Count, threads.Distinct().Count(), "no thread appears twice");

        ulong union = 0;
        foreach (int thread in threads) union |= 1UL << thread;
        Assert.Equal(topology.MachineMask.Value, union, "the union of all cluster threads is the machine mask");

        // Secures that "Copy topology" serialises the very dump the card was built from.
        Assert.Equal(true, ReferenceEquals(topology.Source, snapshot), "Source is passed through, not rebuilt");
    }
}
