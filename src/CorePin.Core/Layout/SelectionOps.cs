using CorePin.Core.Primitives;
using CorePin.Core.Topology;

namespace CorePin.Core.Layout;

/// Pure mask operations of the card; every function returns a new mask and holds no state.
public static class SelectionOps
{
    /// All named threads selected → all removed; otherwise all added.
    public static AffinityMask ToggleThreads(AffinityMask mask, IReadOnlyList<int> threads)
    {
        ArgumentNullException.ThrowIfNull(threads);

        bool allSelected = threads.All(mask.Contains);
        foreach (int thread in threads)
            mask = allSelected ? mask.Without(thread) : mask.With(thread);
        return mask;
    }

    /// Tri-state of the cluster header: complete first, then empty.
    public static AffinityMask ToggleCluster(AffinityMask mask, CpuCluster cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        return ToggleThreads(mask, [.. cluster.Cores.SelectMany(c => c.Threads)]);
    }

    /// Every core that contributes at least one selected thread contributes all of them.
    public static AffinityMask WithSmt(AffinityMask mask, CpuTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        foreach (var core in Cores(topology))
        {
            if (!core.Threads.Any(mask.Contains)) continue;
            foreach (int thread in core.Threads) mask = mask.With(thread);
        }
        return mask;
    }

    /// Every contributing core keeps only its lowest selected thread — never empties a selection.
    public static AffinityMask WithoutSmt(AffinityMask mask, CpuTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        foreach (var core in Cores(topology))
        {
            bool kept = false;      // threads are ascending, so the first selected is the lowest
            foreach (int thread in core.Threads)
            {
                if (!mask.Contains(thread)) continue;
                if (kept) mask = mask.Without(thread);
                else kept = true;
            }
        }
        return mask;
    }

    /// Derived, never stored: on as soon as any selected core contributes two threads.
    public static bool SmtIsOn(AffinityMask selection, CpuTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return Cores(topology).Any(core => core.Threads.Count(selection.Contains) >= 2);
    }

    /// False exactly when toggling could not change anything.
    public static bool SmtIsEnabled(AffinityMask selection, CpuTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return Cores(topology).Any(core => core.Threads.Count >= 2 && core.Threads.Any(selection.Contains));
    }

    public static AffinityMask ClusterMask(CpuCluster cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        return AffinityMask.FromThreads(cluster.Cores.SelectMany(c => c.Threads));
    }

    /// [All] is the machine mask by definition, then one preset per cluster in topology order.
    public static IReadOnlyList<(string Label, AffinityMask Mask)> Presets(CpuTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var presets = new List<(string, AffinityMask)>(topology.Clusters.Count + 1)
        {
            ("All", topology.MachineMask),
        };
        foreach (var cluster in topology.Clusters)
            presets.Add((cluster.Label, ClusterMask(cluster)));
        return presets;
    }

    private static IEnumerable<PhysicalCore> Cores(CpuTopology topology)
        => topology.Clusters.SelectMany(c => c.Cores);
}
