using CorePin.Core.Topology;

namespace CorePin.Tests;

/// Access to the four frozen dumps (S01 §5.2) and small builders for the ad-hoc
/// snapshots of S04 §10.7.
internal static class Fixtures
{
    internal const string Ryzen7945HX = "ryzen-9-7945hx.json";
    internal const string Core14900K = "core-i9-14900k.json";
    internal const string CoreUltra155H = "core-ultra-7-155h.json";
    internal const string Phoenix2 = "ryzen-5-7545u.json";

    internal static string Text(string fileName) => File.ReadAllText(PathOf(fileName));

    /// Raw bytes — ReadAllText would strip a UTF-8 BOM silently, and "no BOM" is part of
    /// the canonical form (S04 §3.4).
    internal static byte[] Bytes(string fileName) => File.ReadAllBytes(PathOf(fileName));

    private static string PathOf(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "data", "topology", fileName);

    internal static TopologySnapshot Load(string fileName) => TopologyJson.Parse(Text(fileName));

    internal static byte[] RawBuffer(string fileName)
        => Convert.FromBase64String(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "raw", fileName)).Trim());

    internal static TopologySnapshot Snapshot(
        string vendor,
        IReadOnlyList<CoreRecord> cores,
        IReadOnlyList<CacheRecord>? caches = null,
        int activeGroupCount = 1,
        string cpuName = "Test CPU",
        string capturedBy = "CorePin 0.1.0",
        bool constructed = true) => new()
        {
            CapturedBy = capturedBy,
            Constructed = constructed,
            Vendor = vendor,
            CpuName = cpuName,
            ActiveGroupCount = activeGroupCount,
            Cores = cores,
            Caches = caches ?? [],
        };

    /// smt defaults to what the mask says, so a test only states it when it disagrees.
    internal static CoreRecord MakeCore(ulong mask, int efficiencyClass = 0, bool? smt = null) => new()
    {
        Mask = mask,
        EfficiencyClass = efficiencyClass,
        Smt = smt ?? System.Numerics.BitOperations.PopCount(mask) > 1,
    };

    internal static CacheRecord MakeCache(ulong mask, long sizeBytes, int level = 3) => new()
    {
        Level = level,
        SizeBytes = sizeBytes,
        Mask = mask,
    };

    internal static string ThreadsOf(CpuCluster cluster)
        => string.Join(",", cluster.Cores.SelectMany(c => c.Threads));

    internal static ulong MaskOf(CpuCluster cluster)
    {
        ulong mask = 0;
        foreach (int thread in cluster.Cores.SelectMany(c => c.Threads)) mask |= 1UL << thread;
        return mask;
    }

    /// Field-wise description of a CpuTopology for the determinism tests (S04 §10.5).
    /// Test-side output only — there is deliberately no CpuTopology serializer in Core.
    internal static string Describe(CpuTopology topology)
        => string.Join(" | ",
            [
                topology.Profiling.ToString(),
                topology.MachineMask.ToHex(),
                topology.LogicalProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                topology.HasSmt ? "smt" : "no-smt",
                string.Join(" ; ", topology.Clusters.Select(c =>
                    $"{c.Label}/{c.Badge ?? "-"}/{c.L3Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{(c.HasL3 ? "l3" : "no-l3")}/["
                    + string.Join(" ", c.Cores.Select(p => string.Join(",", p.Threads))) + "]")),
                string.Join(" ; ", topology.Notes),
            ]);
}
