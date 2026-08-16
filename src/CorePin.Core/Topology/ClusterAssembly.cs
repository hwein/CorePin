namespace CorePin.Core.Topology;

/// Groups the assigned cores into clusters and turns the drafts into the public result type.
internal static class ClusterAssembly
{
    public static List<ClusterDraft> BuildClusters(List<CoreInfo> cores, List<L3Group> groups)
    {
        int virtualCores = cores.Count(c => c.L3GroupIndex < 0);

        var drafts = new List<ClusterDraft>();
        foreach (var core in cores)
        {
            var draft = drafts.Find(d => d.L3GroupIndex == core.L3GroupIndex
                                      && d.EfficiencyClass == core.EfficiencyClass);
            if (draft is null)
            {
                bool hasL3 = core.L3GroupIndex >= 0;
                draft = new ClusterDraft(
                    core.L3GroupIndex,
                    core.EfficiencyClass,
                    hasL3 ? groups[core.L3GroupIndex].SizeBytes : 0,
                    hasL3 ? groups[core.L3GroupIndex].Cores.Count : virtualCores);
                drafts.Add(draft);
            }
            draft.Cores.Add(core);
        }

        foreach (var draft in drafts)
            draft.Cores.Sort((a, b) => a.Threads[0].CompareTo(b.Threads[0]));

        // Explicit sort key, no reliance on grouping order; ties are impossible here.
        return [.. drafts.OrderBy(d => d.Cores.Min(c => c.Threads[0]))];
    }

    public static List<CpuCluster> Materialize(
        List<ClusterDraft> drafts, LabelResult labels, string vendor)
    {
        bool amd = ClusterLabeling.IsAmd(vendor);
        var result = new List<CpuCluster>(drafts.Count);

        for (int i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            // Measured arithmetic, not a labelling level: set on AMD regardless, never Intel.
            bool badge = amd && VCacheRule.HasVCache(draft.L3Bytes, draft.L3GroupPhysicalCores);

            result.Add(new CpuCluster
            {
                Label = labels.Labels[i],
                Badge = badge ? "V-Cache" : null,
                Cores = [.. draft.Cores.Select(c => new PhysicalCore { Threads = c.Threads })],
                L3Bytes = draft.L3Bytes,
                HasL3 = draft.L3GroupIndex >= 0,
            });
        }
        return result;
    }
}
