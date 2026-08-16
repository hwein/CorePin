using System.Numerics;
using CorePin.Core.Primitives;

namespace CorePin.Core.Topology;

public static class ClusterBuilder
{
    /// PURE FUNCTION: no Win32, registry, logger, time, randomness, file system or culture.
    public static CpuTopology Build(TopologySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SnapshotValidation.Validate(snapshot);

        var cores = CoreGrouping.BuildCores(snapshot);
        ulong machineMask = 0;
        foreach (var core in cores) machineMask |= core.Mask;

        var notes = new NoteCollector();
        var groups = CoreGrouping.BuildL3Groups(snapshot, notes);
        CoreGrouping.AssignCores(cores, groups, machineMask, notes);
        groups = CoreGrouping.DropEmptyGroups(cores, groups, notes);

        var clusters = ClusterAssembly.BuildClusters(cores, groups);
        bool hasSmt = CoreGrouping.DetectSmt(snapshot, cores, notes);

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
            Clusters = ClusterAssembly.Materialize(clusters, labels, snapshot.Vendor),
            Source = snapshot,
            Notes = notes.ToList(),
        };
    }
}
