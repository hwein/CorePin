using CorePin.Core.Primitives;
using CorePin.Core.Topology;

namespace CorePin.Core.Layout;

/// The one place that turns a mask into prose — for tooltips and the selection log line.
public static class SelectionDescription
{
    public static string Describe(CpuTopology topology, AffinityMask selection)
    {
        ArgumentNullException.ThrowIfNull(topology);

        if (selection == topology.MachineMask) return "all cores";
        if (UnionLabels(topology, selection, SelectionOps.ClusterMask) is { } clusters) return clusters;
        if (UnionLabels(topology, selection, PhysicalMask) is { } physical) return physical + " (physical)";
        return "custom";
    }

    /// Labels of the clusters whose masks exactly union to the selection; null if none do.
    private static string? UnionLabels(
        CpuTopology topology, AffinityMask selection, Func<CpuCluster, AffinityMask> maskOf)
    {
        var labels = new List<string>();
        var threads = new List<int>();
        foreach (var cluster in topology.Clusters)
        {
            var mask = maskOf(cluster);
            if (!mask.FitsInto(selection)) continue;
            labels.Add(cluster.Label);
            threads.AddRange(mask.ToThreads());
        }
        return labels.Count > 0 && AffinityMask.FromThreads(threads) == selection
            ? string.Join(" + ", labels)
            : null;
    }

    private static AffinityMask PhysicalMask(CpuCluster cluster)
        => AffinityMask.FromThreads(cluster.Cores.Select(c => c.Threads[0]));
}
