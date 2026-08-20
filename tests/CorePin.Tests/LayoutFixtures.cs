using System.Globalization;
using System.Text;
using CorePin.Core.Layout;
using CorePin.Core.Primitives;
using CorePin.Core.Topology;

namespace CorePin.Tests;

/// Shared scaffold of the card suites: reference metrics, built dumps, synthetic topologies.
internal static class LayoutFixtures
{
    internal static readonly string[] AllDumps =
        [Fixtures.Ryzen7945HX, Fixtures.Core14900K, Fixtures.CoreUltra155H, Fixtures.Phoenix2];

    /// The reference measures of the card; labelAdvance is the measured Cascadia Mono advance.
    internal static CardMetrics Metrics(double labelAdvance = 6.45) => new()
    {
        SliceWidth = 20,
        CellHeight = 28,
        SeamWidth = 4,
        CellGap = 4,
        RowGap = 8,
        LabelGap = 4,
        LabelHeight = 16,
        LabelAdvance = labelAdvance,
        HeaderHeight = 28,
        HeaderGap = 8,
        FramePad = 8,
        FrameBorder = 1,
        ClusterGap = 8,
    };

    internal static CpuTopology Topology(string fileName) => ClusterBuilder.Build(Fixtures.Load(fileName));

    internal static CardLayout Build(string fileName, double availableWidth)
        => CardLayoutBuilder.Build(Topology(fileName), availableWidth, Metrics());

    internal static CpuCluster Cluster(string label, string? badge, long l3Bytes, params int[][] cores) => new()
    {
        Label = label,
        Badge = badge,
        Cores = [.. cores.Select(threads => new PhysicalCore { Threads = threads })],
        L3Bytes = l3Bytes,
        HasL3 = l3Bytes > 0,
    };

    internal static CpuTopology Synthetic(params CpuCluster[] clusters)
        => Synthetic(
            AffinityMask.FromThreads(clusters.SelectMany(c => c.Cores).SelectMany(c => c.Threads)),
            clusters);

    internal static CpuTopology Synthetic(AffinityMask machineMask, params CpuCluster[] clusters) => new()
    {
        Vendor = "TestVendor",
        CpuName = "Test CPU",
        LogicalProcessorCount = machineMask.Count,
        MachineMask = machineMask,
        HasSmt = clusters.Any(c => c.Cores.Any(p => p.Threads.Count > 1)),
        Profiling = ProfilingLevel.Profiled,
        Clusters = clusters,
        Source = Fixtures.Snapshot("TestVendor", []),
        Notes = [],
    };

    /// Field-wise flattening for the determinism test — record == compares lists by reference.
    internal static string Dump(CardLayout layout)
    {
        var text = new StringBuilder();
        text.Append(Number(layout.Width)).Append('x').Append(Number(layout.Height));
        foreach (var cluster in layout.Clusters)
        {
            text.Append(" | ").Append(cluster.Index)
                .Append(' ').Append(cluster.HeaderLabel).Append(" / ").Append(cluster.HeaderFacts)
                .Append(' ').Append(Rect(cluster.Frame)).Append(' ').Append(Rect(cluster.Header))
                .Append(" rows=").Append(cluster.RowCount);
            foreach (var cell in cluster.Cells)
            {
                text.Append(" ; ").Append(cell.ClusterIndex).Append('/').Append(cell.CoreIndex)
                    .Append(' ').Append(Rect(cell.Rect)).Append(' ').Append(Rect(cell.LabelRect))
                    .Append(' ').Append(cell.Label).Append(cell.LabelVisible ? "" : "(hidden)");
                foreach (var zone in cell.Zones)
                    text.Append(" z").Append(Rect(zone.Rect))
                        .Append('[').Append(string.Join(",", zone.Threads.Select(Number))).Append(']');
                text.Append(" d[").Append(string.Join(",", cell.Dividers.Select(Number))).Append(']');
            }
        }
        return text.ToString();
    }

    private static string Rect(LayoutRect rect)
        => $"({Number(rect.X)},{Number(rect.Y)},{Number(rect.Width)},{Number(rect.Height)})";

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
