using CorePin.Core.Topology;

namespace CorePin.Tests;

/// The parametrised number test of S04 §10.3 — needs no dump.
public static class VCacheRuleTests
{
    public static void Test_VCacheThreshold()
    {
        (long L3Bytes, int Cores, bool Expected, string Origin)[] cases =
        [
            (100_663_296, 8, true,  "9800X3D, 12 MB per core"),
            (100_663_296, 8, true,  "9950X3D CCD 0"),
            (33_554_432,  8, false, "7945HX / 9950X3D CCD 1, 4 MB per core"),
            (33_554_432,  6, false, "Ryzen 5 5600, 5.3 MB per core"),
            (16_777_216,  6, false, "Phoenix 2, correct reading (2.7 MB per core)"),
            // The most important case: it documents IN the test that the wrong reading
            // (per cluster instead of per L3 group) would produce a badge — the negative
            // probe of the SIGNATURE, not of the result (S04 §10.3).
            (16_777_216,  2, true,  "Phoenix 2, WRONG reading"),
            (37_748_736, 24, false, "14900K, whole ring L3"),
            (8_388_608,   1, true,  "threshold hit exactly (>=)"),
            (8_388_607,   1, false, "one byte below the threshold"),
            (100_663_296, 0, false, "virtual L3 group \"none\" / division by zero"),
            (0,           8, false, "no L3"),
        ];

        foreach (var (l3Bytes, cores, expected, origin) in cases)
        {
            Assert.Equal(expected, VCacheRule.HasVCache(l3Bytes, cores),
                $"HasVCache({l3Bytes}, {cores}) — {origin}");
        }
    }
}
