using CorePin.Core.Layout;
using static CorePin.Tests.LayoutFixtures;

namespace CorePin.Tests;

/// The card geometry: reference platforms, wrapping, monotonicity, labels, determinism.
public static class CardLayoutTests
{
    public static void Test_Ryzen7945HX_At660pxWindow_EightCellsPerRow()
    {
        var layout = Build(Fixtures.Ryzen7945HX, 400);

        Assert.Equal(400.0, layout.Width, "the card takes the full available width");
        Assert.Equal(2, layout.Clusters.Count, "two CCDs");
        foreach (var cluster in layout.Clusters)
        {
            Assert.Equal(1, cluster.RowCount, "one row per CCD");
            Assert.Equal(8, cluster.Cells.Count, "eight cells");
            Assert.Equal(1, cluster.Cells.Select(c => c.Rect.Y).Distinct().Count(), "all cells on one row");
            Assert.Equal(102.0, cluster.Frame.Height, "cluster height");
        }
        Assert.Equal(212.0, layout.Height, "card height");
    }

    public static void Test_Ryzen7945HX_398pxIsTheLowerBound()
    {
        var wide = Build(Fixtures.Ryzen7945HX, 398);
        Assert.Equal(1, wide.Clusters[0].RowCount, "eight cells still fit one row at 398");
        Assert.Equal(212.0, wide.Height, "no wrap at 398");

        var narrow = Build(Fixtures.Ryzen7945HX, 397);
        Assert.Equal(2, narrow.Clusters[0].RowCount, "wraps at 397");
        double firstRowY = narrow.Clusters[0].Cells[0].Rect.Y;
        Assert.Equal(7, narrow.Clusters[0].Cells.Count(c => c.Rect.Y == firstRowY), "seven cells per row at 397");
        Assert.Equal(324.0, narrow.Height, "wrapped card height");
    }

    public static void Test_Core14900K_EClusterInOneRow()
    {
        var layout = Build(Fixtures.Core14900K, 400);

        var pCluster = layout.Clusters[0];
        Assert.Equal(8, pCluster.Cells.Count, "eight P cells");
        Assert.Equal(1, pCluster.RowCount, "P cluster in one row");
        Assert.True(pCluster.Cells.All(c => c.Rect.Width == 44), "SMT cells are 44 wide");

        var eCluster = layout.Clusters[1];
        Assert.Equal(16, eCluster.Cells.Count, "sixteen E cells");
        Assert.Equal(1, eCluster.RowCount, "E cluster in one row");
        Assert.True(eCluster.Cells.All(c => c.Rect.Width == 20), "single cells are 20 wide");

        Assert.Equal(212.0, layout.Height, "card height");
    }

    public static void Test_CoreUltra155H_ThreeClusters()
    {
        var layout = Build(Fixtures.CoreUltra155H, 400);

        Assert.Equal(3, layout.Clusters.Count, "three clusters");
        foreach (var cluster in layout.Clusters)
            Assert.Equal(1, cluster.RowCount, "each cluster in one row");
        Assert.Equal(322.0, layout.Height, "card height");
    }

    public static void Test_Phoenix2_TwoGroups()
    {
        var layout = Build(Fixtures.Phoenix2, 400);

        Assert.Equal(2, layout.Clusters.Count, "two groups");
        Assert.Equal("Group 0", layout.Clusters[0].HeaderLabel, "generic label of the first group");
        Assert.Equal("Group 1", layout.Clusters[1].HeaderLabel, "generic label of the second group");
        Assert.Equal(212.0, layout.Height, "card height");
    }

    public static void Test_CellWidthDependsOnlyOnThreadCount()
    {
        var single = Build(Fixtures.Core14900K, 400).Clusters[1].Cells[0];
        Assert.Equal(20.0, single.Rect.Width, "one thread");
        Assert.Equal(1, single.Zones.Count, "one slice, no seam");

        var smt = Build(Fixtures.Ryzen7945HX, 400).Clusters[0].Cells[0];
        Assert.Equal(44.0, smt.Rect.Width, "two threads");
        Assert.Equal(3, smt.Zones.Count, "slice, seam, slice");

        var triple = CardLayoutBuilder.Build(
            Synthetic(Cluster("CCD 0", null, 0, [0, 1, 2])), 400, Metrics()).Clusters[0].Cells[0];
        Assert.Equal(68.0, triple.Rect.Width, "three threads");
        Assert.Equal(5, triple.Zones.Count, "three slices, two seams");
        Assert.Equal(2, triple.Dividers.Count, "two dividers");
    }

    public static void Test_NoShrinkingAtNarrowWidths()
    {
        foreach (var dump in AllDumps)
        {
            var topology = Topology(dump);
            var metrics = Metrics();
            for (double width = 200; width <= 900; width++)
            {
                var layout = CardLayoutBuilder.Build(topology, width, metrics);
                foreach (var zone in layout.Clusters.SelectMany(c => c.Cells).SelectMany(c => c.Zones))
                {
                    Assert.True(zone.Rect.Height >= 28, $"zone height ({dump} at {width})");
                    if (zone.Threads.Count == 1)
                        Assert.True(zone.Rect.Width >= 20, $"slice width ({dump} at {width})");
                }
            }
        }
    }

    public static void Test_WrapInsteadOfShrink()
    {
        var cluster = Build(Fixtures.Ryzen7945HX, 283).Clusters[0];    // 283 leaves 265 content width

        Assert.Equal(2, cluster.RowCount, "two rows");
        double firstRowY = cluster.Cells[0].Rect.Y;
        Assert.Equal(5, cluster.Cells.Count(c => c.Rect.Y == firstRowY), "five cells in the first row");
        Assert.True(cluster.Cells.All(c => c.Rect.Width == 44), "cell width unchanged");
    }

    public static void Test_LastRowLeftAligned()
    {
        var cluster = Build(Fixtures.Ryzen7945HX, 283).Clusters[0];

        double firstRowY = cluster.Cells[0].Rect.Y;
        var secondRow = cluster.Cells.Where(c => c.Rect.Y != firstRowY).ToList();
        Assert.Equal(3, secondRow.Count, "the remaining three cells fill the second row");
        Assert.Equal(cluster.Cells[0].Rect.X, secondRow[0].Rect.X, "second row starts at the same x");
        Assert.Equal(56.0, secondRow[0].Rect.Y - firstRowY, "row advance is the row block plus the row gap");
    }

    public static void Test_MinimumWindowSize_ReferenceHeights()
    {
        var hx = Build(Fixtures.Ryzen7945HX, 283);
        Assert.Equal(324.0, hx.Height, "7945HX scrolls at the minimum window");
        Assert.True(hx.Clusters.All(c => c.Frame.Height == 158), "both CCDs wrap to two rows");

        var k = Build(Fixtures.Core14900K, 283);
        Assert.Equal(324.0, k.Height, "14900K scrolls at the minimum window");
        double pFirstRowY = k.Clusters[0].Cells[0].Rect.Y;
        Assert.Equal(5, k.Clusters[0].Cells.Count(c => c.Rect.Y == pFirstRowY), "P cells wrap five plus three");
        double eFirstRowY = k.Clusters[1].Cells[0].Rect.Y;
        Assert.Equal(11, k.Clusters[1].Cells.Count(c => c.Rect.Y == eFirstRowY), "E cells wrap eleven plus five");

        var h = Build(Fixtures.CoreUltra155H, 283);
        Assert.Equal(378.0, h.Height, "155H is the tallest reference");
        Assert.Equal("158,102,102", string.Join(",", h.Clusters.Select(c => c.Frame.Height)),
            "only the P cluster wraps");

        Assert.Equal(212.0, Build(Fixtures.Phoenix2, 283).Height, "Phoenix 2 keeps its height at any width");
    }

    public static void Test_ClusterHeightPerRowCount()
    {
        Assert.Equal(102.0, Build(Fixtures.Ryzen7945HX, 400).Clusters[0].Frame.Height, "one row");
        Assert.Equal(158.0, Build(Fixtures.Ryzen7945HX, 283).Clusters[0].Frame.Height, "two rows");

        var eCluster = Build(Fixtures.Core14900K, 188).Clusters[1];
        Assert.Equal(3, eCluster.RowCount, "sixteen cells in rows of seven");
        Assert.Equal(214.0, eCluster.Frame.Height, "three rows");
    }

    public static void Test_HeightMonotoneInWidth()
    {
        foreach (var dump in AllDumps)
        {
            var topology = Topology(dump);
            var metrics = Metrics();
            double previous = double.MaxValue;
            for (double width = 200; width <= 900; width++)
            {
                double height = CardLayoutBuilder.Build(topology, width, metrics).Height;
                Assert.True(height <= previous, $"height must never grow with width ({dump} at {width})");
                previous = height;
            }
        }
    }

    public static void Test_AtLeastOneCellPerRow()
    {
        var layout = Build(Fixtures.Ryzen7945HX, 30);

        foreach (var cluster in layout.Clusters)
        {
            Assert.Equal(8, cluster.RowCount, "one cell per row");
            Assert.Equal(8, cluster.Cells.Select(c => c.Rect.Y).Distinct().Count(), "eight distinct rows");
        }
        Assert.True(double.IsFinite(layout.Height), "height stays finite");
        Assert.Equal(996.0, layout.Height, "stacked height");
    }

    public static void Test_CellGapAndFramePadding()
    {
        var layout = Build(Fixtures.Ryzen7945HX, 400);

        foreach (var cluster in layout.Clusters)
        {
            Assert.Equal(cluster.Frame.X + 9, cluster.Cells[0].Rect.X, "border plus pad before the first cell");
            Assert.Equal(cluster.Frame.Y + 9, cluster.Header.Y, "border plus pad above the header");
            Assert.Equal(8.0, cluster.Cells[0].Rect.Y - cluster.Header.Bottom, "header gap above the cells");
            for (int i = 1; i < cluster.Cells.Count; i++)
                Assert.Equal(4.0, cluster.Cells[i].Rect.X - cluster.Cells[i - 1].Rect.Right, "cell gap");
            foreach (var cell in cluster.Cells)
            {
                Assert.True(cell.Rect.X >= cluster.Frame.X && cell.Rect.Right <= cluster.Frame.Right,
                    "cell inside the frame");
                Assert.True(cell.LabelRect.Bottom <= cluster.Frame.Bottom, "label inside the frame");
            }
        }
        Assert.Equal(8.0, layout.Clusters[1].Frame.Y - layout.Clusters[0].Frame.Bottom, "cluster gap");

        var overflow = Build(Fixtures.Ryzen7945HX, 30);
        Assert.True(overflow.Clusters[0].Cells[0].Rect.Right > overflow.Clusters[0].Frame.Right,
            "an over-wide cell overflows the frame instead of shrinking");
    }

    public static void Test_LabelsAreThreadIndices()
    {
        var hx = Build(Fixtures.Ryzen7945HX, 400);
        Assert.Equal("0·1", hx.Clusters[0].Cells[0].Label, "first cell of CCD 0");
        Assert.Equal("30·31", hx.Clusters[1].Cells[7].Label, "last cell of CCD 1");

        var eCluster = Build(Fixtures.Core14900K, 400).Clusters[1];
        Assert.Equal("16", eCluster.Cells[0].Label, "first E cell");
        Assert.Equal("17", eCluster.Cells[1].Label, "second E cell");
        Assert.Equal("31", eCluster.Cells[15].Label, "last E cell");
    }

    public static void Test_LabelStride_NotTriggeredWithCascadia()
    {
        foreach (var dump in AllDumps)
        {
            var layout = Build(dump, 400);
            Assert.True(layout.Clusters.SelectMany(c => c.Cells).All(c => c.LabelVisible),
                $"every label stays visible ({dump})");
        }
    }

    public static void Test_LabelStride_TriggeredByWiderFont()
    {
        var topology = Topology(Fixtures.Ryzen7945HX);

        var cells = CardLayoutBuilder.Build(topology, 400, Metrics(labelAdvance: 12)).Clusters[1].Cells;
        for (int i = 0; i < cells.Count; i++)
            Assert.Equal(i % 2 == 0, cells[i].LabelVisible, $"labels alternate, cell {i}");
        Assert.True(cells.All(c => c.Rect.Width == 44), "cell width unchanged");

        var wrapped = CardLayoutBuilder.Build(topology, 283, Metrics(labelAdvance: 12)).Clusters[0];
        Assert.Equal(false, wrapped.Cells[1].LabelVisible, "second label of a row is hidden");
        Assert.Equal(true, wrapped.Cells[5].LabelVisible, "each row restarts with a visible label");
    }

    public static void Test_Determinism()
    {
        var topology = Topology(Fixtures.CoreUltra155H);
        var metrics = Metrics();

        Assert.Equal(
            Dump(CardLayoutBuilder.Build(topology, 400, metrics)),
            Dump(CardLayoutBuilder.Build(topology, 400, metrics)),
            "two builds with the same input are structurally identical");
    }

    public static void Test_FramesAlwaysSpanTheAvailableWidth()
    {
        var wide = Build(Fixtures.Ryzen7945HX, 10_000);
        Assert.Equal(10_000.0, wide.Width, "the card takes the whole available width");
        Assert.True(wide.Clusters.All(c => c.Frame.Width == 10_000), "every frame spans it");
        Assert.Equal(212.0, wide.Height, "extra width never changes the height");

        Assert.Equal(400.0, Build(Fixtures.CoreUltra155H, 400).Width, "a narrow topology still fills the card");
        Assert.Equal(400.0, Build(Fixtures.Phoenix2, 400).Width, "even with four cells per cluster");
        Assert.Equal(300.0, Build(Fixtures.Ryzen7945HX, 300).Width, "a narrow card narrows the frames");
    }
}
