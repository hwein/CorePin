using System.Globalization;

namespace CorePin.Core.Topology;

internal readonly record struct LabelResult(string[] Labels, ProfilingLevel Profiling);

/// Decides Label and Profiling only; the badge is arithmetic and lives in VCacheRule.
internal static class ClusterLabeling
{
    private const string Amd = "AuthenticAMD";
    private const string Intel = "GenuineIntel";

    internal static bool IsAmd(string vendor)
        => string.Equals(vendor.Trim(), Amd, StringComparison.OrdinalIgnoreCase);

    private static bool IsIntel(string vendor)
        => string.Equals(vendor.Trim(), Intel, StringComparison.OrdinalIgnoreCase);

    internal static LabelResult Assign(
        string vendor,
        IReadOnlyList<ClusterDraft> clusters,
        bool hasStructureNote,
        IReadOnlyList<CoreInfo> cores)
    {
        // The only place where the vendor name has an effect.
        if (IsAmd(vendor))
        {
            if (AmdLabels(clusters, hasStructureNote, cores) is { } amd)
                return new LabelResult(amd, ProfilingLevel.Profiled);
        }
        else if (IsIntel(vendor))
        {
            if (IntelLabels(clusters, hasStructureNote, cores) is { } intel)
                return new LabelResult(intel, ProfilingLevel.Profiled);
        }

        return new LabelResult(Generic(clusters.Count), ProfilingLevel.NotProfiled);
    }

    private static string[]? AmdLabels(
        IReadOnlyList<ClusterDraft> clusters, bool hasStructureNote, IReadOnlyList<CoreInfo> cores)
    {
        if (hasStructureNote) return null;
        if (EfficiencyClassCount(cores) != 1) return null;
        if (cores.Any(c => c.L3GroupIndex < 0)) return null;

        // Here each cluster is exactly one L3 group, so the group rank is the cluster rank.
        var labels = new string[clusters.Count];
        for (int i = 0; i < clusters.Count; i++)
            labels[i] = "CCD " + clusters[i].L3GroupIndex.ToString(CultureInfo.InvariantCulture);
        return labels;
    }

    private static string[]? IntelLabels(
        IReadOnlyList<ClusterDraft> clusters, bool hasStructureNote, IReadOnlyList<CoreInfo> cores)
    {
        // Holds in both Intel cases and in the AMD branch, so it stands before the split.
        if (hasStructureNote) return null;

        if (EfficiencyClassCount(cores) == 1)
        {
            // Case A — up to 11th gen, pure P-core parts.
            if (clusters.Count != 1 || clusters[0].L3GroupIndex < 0) return null;
            return ["Cores"];
        }

        // Case B — at least two efficiency classes.
        int maxClass = cores.Max(c => c.EfficiencyClass);
        var labels = new string[clusters.Count];
        for (int i = 0; i < clusters.Count; i++)
        {
            // Rule 1 first: EfficiencyClass cannot separate E from LP-E; the missing L3 can.
            if (clusters[i].L3GroupIndex < 0)
            {
                if (clusters[i].EfficiencyClass == maxClass) return null;
                labels[i] = "LP E-Cores";
            }
            else
            {
                labels[i] = clusters[i].EfficiencyClass == maxClass ? "P-Cores" : "E-Cores";
            }
        }

        if (labels.Distinct(StringComparer.Ordinal).Count() != labels.Length) return null;
        return labels;
    }

    private static string[] Generic(int count)
    {
        var labels = new string[count];
        for (int i = 0; i < count; i++)
            labels[i] = "Group " + i.ToString(CultureInfo.InvariantCulture);
        return labels;
    }

    private static int EfficiencyClassCount(IReadOnlyList<CoreInfo> cores)
        => cores.Select(c => c.EfficiencyClass).Distinct().Count();
}
