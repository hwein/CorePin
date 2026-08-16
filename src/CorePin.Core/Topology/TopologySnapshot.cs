namespace CorePin.Core.Topology;

/// The raw dump format — there is only this one, for fixtures and for --dump-topology.
public sealed record TopologySnapshot
{
    public required string CapturedBy { get; init; }        // "CorePin 0.1.0"
    public required bool Constructed { get; init; }
    public required string Vendor { get; init; }            // AuthenticAMD | GenuineIntel | …
    public required string CpuName { get; init; }
    public required int ActiveGroupCount { get; init; }
    public required IReadOnlyList<CoreRecord> Cores { get; init; }
    public required IReadOnlyList<CacheRecord> Caches { get; init; }
}

/// One record per GROUP_AFFINITY entry of a RelationProcessorCore record.
public sealed record CoreRecord
{
    public required ulong Mask { get; init; }
    public required int EfficiencyClass { get; init; }
    public required bool Smt { get; init; }
}

/// One record per GROUP_AFFINITY entry of a RelationCache record, all levels.
public sealed record CacheRecord
{
    public required int Level { get; init; }
    public required long SizeBytes { get; init; }
    public required ulong Mask { get; init; }
}
