using System.Numerics;
using static CorePin.Core.Topology.MaskFormat;

namespace CorePin.Core.Topology;

/// Turns core and cache records into physical cores and the L3 groups they sit in.
internal static class CoreGrouping
{
    public static List<CoreInfo> BuildCores(TopologySnapshot snapshot)
    {
        var result = new List<CoreInfo>(snapshot.Cores.Count);
        foreach (var record in snapshot.Cores)
            result.Add(new CoreInfo(record.Mask, record.EfficiencyClass, Threads(record.Mask)));
        return result;
    }

    public static List<L3Group> BuildL3Groups(TopologySnapshot snapshot, NoteCollector notes)
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

        // Only to make AssignCores deterministic; the final order comes from DropEmptyGroups.
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

    public static void AssignCores(
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

    public static List<L3Group> DropEmptyGroups(
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

    public static bool DetectSmt(TopologySnapshot snapshot, List<CoreInfo> cores, NoteCollector notes)
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
}
