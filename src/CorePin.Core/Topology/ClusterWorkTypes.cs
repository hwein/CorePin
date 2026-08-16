namespace CorePin.Core.Topology;

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
