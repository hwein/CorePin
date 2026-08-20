using System.Globalization;
using CorePin.Core.Topology;

namespace CorePin.Core.Layout;

public static class CardLayoutBuilder
{
    /// PURE FUNCTION: no WPF, no state, no time, no culture-dependent formatting.
    public static CardLayout Build(CpuTopology topology, double availableWidth, CardMetrics m)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(m);

        double framing = 2 * m.FrameBorder + 2 * m.FramePad;

        // Width is capped at the widest single-row layout — anything wider would only stretch empty frames.
        double naturalWidth = topology.Clusters.Max(c => c.Cores.Count * Pitch(c, m) - m.CellGap) + framing;
        double width = Math.Min(availableWidth, naturalWidth);
        double contentWidth = width - framing;

        var clusters = new List<ClusterBox>(topology.Clusters.Count);
        double y = 0;
        for (int i = 0; i < topology.Clusters.Count; i++)
        {
            if (i > 0) y += m.ClusterGap;
            var cluster = BuildCluster(topology.Clusters[i], i, y, width, contentWidth, m);
            clusters.Add(cluster);
            y = cluster.Frame.Bottom;
        }

        return new CardLayout(width, y, clusters, m);
    }

    private static ClusterBox BuildCluster(
        CpuCluster cluster, int index, double top, double width, double contentWidth, CardMetrics m)
    {
        double pitch = Pitch(cluster, m);
        int perRow = Math.Max(1, (int)Math.Floor((contentWidth + m.CellGap) / pitch));
        int rowCount = (cluster.Cores.Count + perRow - 1) / perRow;

        double rowBlock = m.CellHeight + m.LabelGap + m.LabelHeight;
        double height = 2 * m.FrameBorder + 2 * m.FramePad + m.HeaderHeight + m.HeaderGap
                        + rowCount * rowBlock + (rowCount - 1) * m.RowGap;

        double contentX = m.FrameBorder + m.FramePad;
        double contentY = top + m.FrameBorder + m.FramePad;

        var labels = new string[cluster.Cores.Count];
        for (int i = 0; i < labels.Length; i++) labels[i] = LabelOf(cluster.Cores[i]);
        int stride = LabelStride(labels, pitch, m);

        var cells = new List<CellBox>(cluster.Cores.Count);
        double firstRowY = contentY + m.HeaderHeight + m.HeaderGap;
        for (int i = 0; i < cluster.Cores.Count; i++)
        {
            int column = i % perRow;
            double x = contentX + column * pitch;
            double cellY = firstRowY + (i / perRow) * (rowBlock + m.RowGap);
            bool labelVisible = stride == 1 || column % 2 == 0;
            cells.Add(BuildCell(cluster.Cores[i], index, i, x, cellY, labels[i], labelVisible, m));
        }

        return new ClusterBox(
            index,
            new LayoutRect(0, top, width, height),
            new LayoutRect(contentX, contentY, contentWidth, m.HeaderHeight),
            cluster.Label,
            HeaderFacts(cluster),
            cells,
            rowCount);
    }

    private static CellBox BuildCell(
        PhysicalCore core, int clusterIndex, int coreIndex,
        double x, double y, string label, bool labelVisible, CardMetrics m)
    {
        int threadCount = core.Threads.Count;
        double width = CellWidth(threadCount, m);
        double step = m.SliceWidth + m.SeamWidth;

        var zones = new List<CellZone>(2 * threadCount - 1);
        var dividers = new List<double>(threadCount - 1);
        for (int i = 0; i < threadCount; i++)
        {
            double sliceX = x + i * step;
            zones.Add(new CellZone(
                new LayoutRect(sliceX, y, m.SliceWidth, m.CellHeight), [core.Threads[i]]));
            if (i == threadCount - 1) continue;

            double seamX = sliceX + m.SliceWidth;
            zones.Add(new CellZone(
                new LayoutRect(seamX, y, m.SeamWidth, m.CellHeight), [core.Threads[i], core.Threads[i + 1]]));
            dividers.Add(seamX + m.SeamWidth / 2);
        }

        return new CellBox(
            clusterIndex,
            coreIndex,
            new LayoutRect(x, y, width, m.CellHeight),
            zones,
            dividers,
            new LayoutRect(x, y + m.CellHeight + m.LabelGap, width, m.LabelHeight),
            label,
            labelVisible);
    }

    /// The grid step of a cluster; all its cells follow the width of the first core.
    private static double Pitch(CpuCluster cluster, CardMetrics m)
        => CellWidth(cluster.Cores[0].Threads.Count, m) + m.CellGap;

    private static double CellWidth(int threadCount, CardMetrics m)
        => threadCount * m.SliceWidth + (threadCount - 1) * m.SeamWidth;

    private static string LabelOf(PhysicalCore core)
        => string.Join("·", core.Threads.Select(t => t.ToString(CultureInfo.InvariantCulture)));

    /// 2 = draw only every second label of a row, because the widest label would overlap its neighbour.
    private static int LabelStride(string[] labels, double pitch, CardMetrics m)
    {
        int maxLength = 0;
        foreach (var label in labels) maxLength = Math.Max(maxLength, label.Length);
        return maxLength * m.LabelAdvance + m.LabelGap > pitch ? 2 : 1;
    }

    private static string HeaderFacts(CpuCluster cluster)
    {
        var parts = new List<string>(3);
        if (cluster.Badge is not null) parts.Add(cluster.Badge);
        parts.Add(cluster.PhysicalCoreCount.ToString(CultureInfo.InvariantCulture)
                  + "C/" + cluster.LogicalCount.ToString(CultureInfo.InvariantCulture) + "T");
        if (cluster.HasL3) parts.Add(L3Megabytes(cluster.L3Bytes) + " MB L3");
        return string.Join(" · ", parts);
    }

    private static string L3Megabytes(long bytes)
    {
        const long mebibyte = 1_048_576;
        return bytes % mebibyte == 0
            ? (bytes / mebibyte).ToString(CultureInfo.InvariantCulture)
            : (bytes / (double)mebibyte).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
