namespace CorePin.Core.Topology;

/// The whole V-Cache rule (S04 §5.3): L3 size divided by the PHYSICAL cores of the
/// L3 GROUP must reach 8 MiB. Both qualifiers matter — per cluster, Phoenix 2 would get
/// a badge it must not have; per thread, the 9800X3D would lose the one it must have.
public static class VCacheRule
{
    private const long BytesPerCore = 8L * 1024 * 1024;

    /// Integer arithmetic, no division: no rounding loss, no division by zero, and the
    /// threshold is exactly reproducible.
    public static bool HasVCache(long l3Bytes, int physicalCoresOfL3Group)
        => physicalCoresOfL3Group > 0
        && l3Bytes >= BytesPerCore * physicalCoresOfL3Group;
}
