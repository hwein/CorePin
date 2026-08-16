using System.Globalization;
using static CorePin.Core.Topology.MaskFormat;

namespace CorePin.Core.Topology;

/// Rejects what no clustering could repair; everything survivable becomes a note instead.
internal static class SnapshotValidation
{
    public static void Validate(TopologySnapshot snapshot)
    {
        if (snapshot.ActiveGroupCount != 1)
            throw new TopologyFormatException(
                $"activeGroupCount is {snapshot.ActiveGroupCount}, only single-group systems can be clustered");

        if (snapshot.Cores.Count == 0)
            throw new TopologyFormatException("cores is empty, there is no machine mask to build");

        ulong seen = 0;
        foreach (var core in snapshot.Cores)
        {
            if (core.Mask == 0)
                throw new TopologyFormatException("a core record has mask 0x0000000000000000");
            if ((seen & core.Mask) != 0)
                throw new TopologyFormatException(
                    $"core masks overlap at {Hex(seen & core.Mask)}, a logical processor cannot belong to two cores");
            seen |= core.Mask;

            if (core.EfficiencyClass < 0)
                throw new TopologyFormatException(
                    $"core {Hex(core.Mask)} has negative efficiencyClass {core.EfficiencyClass.ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (var cache in snapshot.Caches)
        {
            if (cache.Level < 1)
                throw new TopologyFormatException(
                    $"cache {Hex(cache.Mask)} has level {cache.Level.ToString(CultureInfo.InvariantCulture)}");
            if (cache.SizeBytes < 0)
                throw new TopologyFormatException(
                    $"cache {Hex(cache.Mask)} has negative sizeBytes {cache.SizeBytes.ToString(CultureInfo.InvariantCulture)}");
        }
    }
}
