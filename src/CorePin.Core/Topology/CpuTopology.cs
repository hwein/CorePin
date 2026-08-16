using CorePin.Core.Primitives;

namespace CorePin.Core.Topology;

public enum ProfilingLevel { Profiled, NotProfiled }

public sealed record CpuTopology
{
    public required string Vendor { get; init; }
    public required string CpuName { get; init; }
    public required int LogicalProcessorCount { get; init; }
    public required AffinityMask MachineMask { get; init; }
    public required bool HasSmt { get; init; }
    public required ProfilingLevel Profiling { get; init; }
    public required IReadOnlyList<CpuCluster> Clusters { get; init; }

    /// The dump this was built from — the data source of "Copy topology".
    public required TopologySnapshot Source { get; init; }

    /// Diagnostics of the cluster building as DATA — CorePin.Core.Topology must not log.
    public required IReadOnlyList<string> Notes { get; init; }
}

public sealed record CpuCluster
{
    public required string Label { get; init; }         // "CCD 0" | "P-Cores" | "Group 1" …
    public required string? Badge { get; init; }        // "V-Cache" or null
    public required IReadOnlyList<PhysicalCore> Cores { get; init; }
    public required long L3Bytes { get; init; }         // 0 = no L3
    public required bool HasL3 { get; init; }
    public int PhysicalCoreCount => Cores.Count;
    public int LogicalCount => Cores.Sum(c => c.Threads.Count);
}

public sealed record PhysicalCore
{
    // at least 1, with SMT 2 — more is allowed and is not rejected
    public required IReadOnlyList<int> Threads { get; init; }
}
