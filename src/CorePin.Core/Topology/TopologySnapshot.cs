namespace CorePin.Core.Topology;

/// Raw format: exactly the JSON of 02 §4.6. Input of the frozen fixtures, output of
/// --dump-topology and "Copy topology". There is only this one format (S01 §3.2).
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

/// One record per GROUP_AFFINITY entry of a RelationProcessorCore record (S04 §2.4).
public sealed record CoreRecord
{
    public required ulong Mask { get; init; }
    public required int EfficiencyClass { get; init; }
    public required bool Smt { get; init; }
}

/// One record per GROUP_AFFINITY entry of a RelationCache record, all levels (S04 §2.5).
public sealed record CacheRecord
{
    public required int Level { get; init; }
    public required long SizeBytes { get; init; }
    public required ulong Mask { get; init; }
}
