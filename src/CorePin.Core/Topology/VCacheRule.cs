namespace CorePin.Core.Topology;

/// 8 MiB per PHYSICAL core of the L3 GROUP — per cluster or per thread both badge wrongly.
public static class VCacheRule
{
    private const long BytesPerCore = 8L * 1024 * 1024;

    /// Multiplication, not division: no rounding loss and no division by zero.
    public static bool HasVCache(long l3Bytes, int physicalCoresOfL3Group)
        => physicalCoresOfL3Group > 0
        && l3Bytes >= BytesPerCore * physicalCoresOfL3Group;
}
