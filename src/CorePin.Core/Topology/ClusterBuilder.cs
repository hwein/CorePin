using System.Globalization;
using System.Numerics;
using CorePin.Core.Primitives;

namespace CorePin.Core.Topology;

public static class ClusterBuilder
{
    /// PURE FUNCTION: no Win32, registry, logger, time, randomness, file system or culture.
    public static CpuTopology Build(TopologySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Validate(snapshot);

        var cores = BuildCores(snapshot);
        ulong machineMask = 0;
        foreach (var core in cores) machineMask |= core.Mask;

        var notes = new NoteCollector();
        var groups = BuildL3Groups(snapshot, notes);           // 1b
        AssignCores(cores, groups, machineMask, notes);        // 1c
        groups = DropEmptyGroups(cores, groups, notes);        // 1d + 1e + remap

        var clusters = BuildClusters(cores, groups);           // step 2 + 3 + 4
        bool hasSmt = DetectSmt(snapshot, cores, notes);

        var labels = ClusterLabeling.Assign(
            snapshot.Vendor, clusters, notes.HasStructureNote, cores);

        return new CpuTopology
        {
            Vendor = snapshot.Vendor,
            CpuName = snapshot.CpuName,
            LogicalProcessorCount = BitOperations.PopCount(machineMask),
            MachineMask = new AffinityMask(machineMask),
            HasSmt = hasSmt,
            Profiling = labels.Profiling,
            Clusters = Materialize(clusters, labels, snapshot.Vendor),
            Source = snapshot,
            Notes = notes.ToList(),
        };
    }

    private static void Validate(TopologySnapshot snapshot)
    {
        if (snapshot.ActiveGroupCount != 1)
            throw new TopologyFormatException(
                $"activeGroupCount is {snapshot.ActiveGroupCount}, only single-group systems can be clustered");

        if (snapshot.Cores.Count == 0)
            throw new TopologyFormatException("cores is empty, there is no machine mask to build");

        ulong seen = 0;
        foreach (var core in snapshot.Cores)
        {
            if (core.Mask == 0)
                throw new TopologyFormatException("a core record has mask 0x0000000000000000");
            if ((seen & core.Mask) != 0)
                throw new TopologyFormatException(
                    $"core masks overlap at {Hex(seen & core.Mask)}, a logical processor cannot belong to two cores");
            seen |= core.Mask;

            if (core.EfficiencyClass < 0)
                throw new TopologyFormatException(
                    $"core {Hex(core.Mask)} has negative efficiencyClass {core.EfficiencyClass.ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (var cache in snapshot.Caches)
        {
            if (cache.Level < 1)
                throw new TopologyFormatException(
                    $"cache {Hex(cache.Mask)} has level {cache.Level.ToString(CultureInfo.InvariantCulture)}");
            if (cache.SizeBytes < 0)
                throw new TopologyFormatException(
                    $"cache {Hex(cache.Mask)} has negative sizeBytes {cache.SizeBytes.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static List<CoreInfo> BuildCores(TopologySnapshot snapshot)
    {
        var result = new List<CoreInfo>(snapshot.Cores.Count);
        foreach (var record in snapshot.Cores)
            result.Add(new CoreInfo(record.Mask, record.EfficiencyClass, Threads(record.Mask)));
        return result;
    }

    private static int[] Threads(ulong mask)
    {
        var threads = new int[BitOperations.PopCount(mask)];
        int index = 0;
        ulong rest = mask;
        while (rest != 0)
        {
            threads[index++] = BitOperations.TrailingZeroCount(rest);
            rest &= rest - 1;
        }
        return threads;
    }

    private static List<L3Group> BuildL3Groups(TopologySnapshot snapshot, NoteCollector notes)
    {
        var groups = new List<L3Group>();
        foreach (var cache in snapshot.Caches)
        {
            if (cache.Level != 3) continue;

            var existing = groups.Find(g => g.Mask == cache.Mask);
            if (existing is not null)
            {
                // Same mask means the same cache; the maximum invents nothing.
                existing.SizeBytes = Math.Max(existing.SizeBytes, cache.SizeBytes);
                notes.Add(NoteStep.L3Groups, cache.Mask, $"l3: duplicate mask {Hex(cache.Mask)}");
            }
            else
            {
                groups.Add(new L3Group(cache.Mask, cache.SizeBytes));
            }
        }

        // Only to make step 1c deterministic; the final order comes from 1e.
        groups = [.. groups.OrderBy(g => BitOperations.TrailingZeroCount(g.Mask))];

        for (int i = 0; i < groups.Count; i++)
        {
            for (int j = i + 1; j < groups.Count; j++)
            {
                if ((groups[i].Mask & groups[j].Mask) == 0) continue;
                ulong low = Math.Min(groups[i].Mask, groups[j].Mask);
                ulong high = Math.Max(groups[i].Mask, groups[j].Mask);
                notes.Add(NoteStep.L3Groups, low, $"l3: overlapping masks {Hex(low)} {Hex(high)}", structural: true);
            }
        }

        return groups;
    }

    private static void AssignCores(
        List<CoreInfo> cores, List<L3Group> groups, ulong machineMask, NoteCollector notes)
    {
        foreach (var core in cores)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if ((groups[i].Mask & core.Mask) == 0) continue;

                core.L3GroupIndex = i;
                groups[i].Cores.Add(core);
                if ((core.Mask & groups[i].Mask) != core.Mask)
                {
                    // Half-attached core: no pattern we can name, but the only sensible fit.
                    notes.Add(NoteStep.CoreAssignment, core.Mask,
                        $"l3: partial core coverage {Hex(core.Mask)}", structural: true);
                }
                break;
            }
        }

        foreach (var group in groups)
        {
            if ((group.Mask & ~machineMask) != 0)
                notes.Add(NoteStep.CoreAssignment, group.Mask,
                    $"l3: mask covers unknown processors {Hex(group.Mask)}");
        }
    }

    private static List<L3Group> DropEmptyGroups(
        List<CoreInfo> cores, List<L3Group> groups, NoteCollector notes)
    {
        var kept = new List<L3Group>(groups.Count);
        foreach (var group in groups)
        {
            if (group.Cores.Count == 0)
            {
                notes.Add(NoteStep.EmptyGroups, group.Mask,
                    $"l3: group without cores {Hex(group.Mask)}", structural: true);
                continue;
            }
            kept.Add(group);
        }

        // Same key as the cluster order; the ThenBy is unreachable, groups being disjoint.
        var ordered = kept
            .OrderBy(g => g.Cores.Min(c => c.Threads[0]))
            .ThenBy(g => BitOperations.TrailingZeroCount(g.Mask))
            .ToList();

        foreach (var core in cores)
            core.L3GroupIndex = core.L3GroupIndex < 0 ? -1 : ordered.IndexOf(groups[core.L3GroupIndex]);

        return ordered;
    }

    private static List<ClusterDraft> BuildClusters(List<CoreInfo> cores, List<L3Group> groups)
    {
        int virtualCores = cores.Count(c => c.L3GroupIndex < 0);

        var drafts = new List<ClusterDraft>();
        foreach (var core in cores)
        {
            var draft = drafts.Find(d => d.L3GroupIndex == core.L3GroupIndex
                                      && d.EfficiencyClass == core.EfficiencyClass);
            if (draft is null)
            {
                bool hasL3 = core.L3GroupIndex >= 0;
                draft = new ClusterDraft(
                    core.L3GroupIndex,
                    core.EfficiencyClass,
                    hasL3 ? groups[core.L3GroupIndex].SizeBytes : 0,
                    hasL3 ? groups[core.L3GroupIndex].Cores.Count : virtualCores);
                drafts.Add(draft);
            }
            draft.Cores.Add(core);
        }

        foreach (var draft in drafts)
            draft.Cores.Sort((a, b) => a.Threads[0].CompareTo(b.Threads[0]));

        // Explicit sort key, no reliance on grouping order; ties are impossible here.
        return [.. drafts.OrderBy(d => d.Cores.Min(c => c.Threads[0]))];
    }

    private static bool DetectSmt(TopologySnapshot snapshot, List<CoreInfo> cores, NoteCollector notes)
    {
        bool hasSmt = false;
        for (int i = 0; i < cores.Count; i++)
        {
            bool maskSaysSmt = cores[i].Threads.Length > 1;
            hasSmt |= maskSaysSmt;

            // The mask is what gets pinned and drawn; the flag is trimming.
            if (maskSaysSmt != snapshot.Cores[i].Smt)
                notes.Add(NoteStep.Smt, cores[i].Mask,
                    $"core: smt flag contradicts mask {Hex(cores[i].Mask)}");
        }
        return hasSmt;
    }

    private static List<CpuCluster> Materialize(
        List<ClusterDraft> drafts, LabelResult labels, string vendor)
    {
        bool amd = ClusterLabeling.IsAmd(vendor);
        var result = new List<CpuCluster>(drafts.Count);

        for (int i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            // Measured arithmetic, not a labelling level: set on AMD regardless, never Intel.
            bool badge = amd && VCacheRule.HasVCache(draft.L3Bytes, draft.L3GroupPhysicalCores);

            result.Add(new CpuCluster
            {
                Label = labels.Labels[i],
                Badge = badge ? "V-Cache" : null,
                Cores = [.. draft.Cores.Select(c => new PhysicalCore { Threads = c.Threads })],
                L3Bytes = draft.L3Bytes,
                HasL3 = draft.L3GroupIndex >= 0,
            });
        }
        return result;
    }

    internal static string Hex(ulong value)
        => "0x" + value.ToString("X16", CultureInfo.InvariantCulture);
}

internal sealed class CoreInfo(ulong mask, int efficiencyClass, int[] threads)
{
    public ulong Mask { get; } = mask;
    public int EfficiencyClass { get; } = efficiencyClass;
    public int[] Threads { get; } = threads;
    public int L3GroupIndex { get; set; } = -1;      // -1 = virtual L3 group "none"
}

internal sealed class L3Group(ulong mask, long sizeBytes)
{
    public ulong Mask { get; } = mask;
    public long SizeBytes { get; set; } = sizeBytes;
    public List<CoreInfo> Cores { get; } = [];
}

internal sealed class ClusterDraft(int l3GroupIndex, int efficiencyClass, long l3Bytes, int l3GroupPhysicalCores)
{
    public int L3GroupIndex { get; } = l3GroupIndex;
    public int EfficiencyClass { get; } = efficiencyClass;
    public long L3Bytes { get; } = l3Bytes;
    public int L3GroupPhysicalCores { get; } = l3GroupPhysicalCores;
    public List<CoreInfo> Cores { get; } = [];
}

internal enum NoteStep { L3Groups, CoreAssignment, EmptyGroups, Smt }

/// Ordered so that two runs produce the same list.
internal sealed class NoteCollector
{
    private readonly List<(NoteStep Step, ulong Mask, string Text)> _notes = [];

    public bool HasStructureNote { get; private set; }

    public void Add(NoteStep step, ulong mask, string text, bool structural = false)
    {
        _notes.Add((step, mask, text));
        if (structural) HasStructureNote = true;
    }

    public IReadOnlyList<string> ToList()
        => [.. _notes.OrderBy(n => n.Step).ThenBy(n => n.Mask).ThenBy(n => n.Text, StringComparer.Ordinal)
                     .Select(n => n.Text)];
}
