using CorePin.Core.Layout;
using static CorePin.Tests.LayoutFixtures;

namespace CorePin.Tests;

/// Hit testing over built layouts: zones, seams, half-open boundaries, dead space.
public static class CardHitTestTests
{
    public static void Test_ZoneCenter_HitsExactlyThatZone()
    {
        foreach (var dump in AllDumps)
        {
            var layout = Build(dump, 400);
            foreach (var cluster in layout.Clusters)
            foreach (var cell in cluster.Cells)
            foreach (var zone in cell.Zones)
            {
                var hit = CardHitTest.At(
                    layout, zone.Rect.X + zone.Rect.Width / 2, zone.Rect.Y + zone.Rect.Height / 2);
                Assert.Equal(HitKind.Threads, hit.Kind, $"zone center hits threads ({dump})");
                Assert.Equal(cluster.Index, hit.ClusterIndex, $"cluster index ({dump})");
                Assert.Equal(
                    string.Join(",", zone.Threads), string.Join(",", hit.Threads),
                    $"exactly this zone's threads ({dump})");
            }
        }
    }

    public static void Test_SeamCenter_HitsBothThreads()
    {
        var layout = Build(Fixtures.Ryzen7945HX, 400);
        var cell = layout.Clusters[0].Cells[0];

        var hit = CardHitTest.At(layout, cell.Rect.X + 22, cell.Rect.Y + 14);
        Assert.Equal(HitKind.Threads, hit.Kind, "seam center hits");
        Assert.Equal("0,1", string.Join(",", hit.Threads), "both threads of the cell");
    }

    public static void Test_Boundaries_HalfOpenToTheRight()
    {
        var layout = Build(Fixtures.Ryzen7945HX, 400);
        var cell = layout.Clusters[0].Cells[0];
        double y = cell.Rect.Y + 14;

        Assert.Equal("0", ThreadsAt(layout, cell.Rect.X + 19.99, y), "just left of the seam");
        Assert.Equal("0,1", ThreadsAt(layout, cell.Rect.X + 20.0, y), "first point of the seam");
        Assert.Equal("0,1", ThreadsAt(layout, cell.Rect.X + 23.99, y), "last point of the seam");
        Assert.Equal("1", ThreadsAt(layout, cell.Rect.X + 24.0, y), "first point of the second slice");
    }

    public static void Test_CellGap_HitsNothing()
    {
        var layout = Build(Fixtures.Ryzen7945HX, 400);
        var first = layout.Clusters[0].Cells[0];

        AssertNone(CardHitTest.At(layout, first.Rect.Right + 2, first.Rect.Y + 14), "between two cells");
    }

    public static void Test_LabelRow_HitsNothing()
    {
        var layout = Build(Fixtures.Ryzen7945HX, 400);
        var label = layout.Clusters[0].Cells[3].LabelRect;

        AssertNone(
            CardHitTest.At(layout, label.X + label.Width / 2, label.Y + label.Height / 2),
            "the label row is information, not a control");
    }

    public static void Test_BetweenClusters_HitsNothing()
    {
        var layout = Build(Fixtures.Ryzen7945HX, 400);

        AssertNone(
            CardHitTest.At(layout, 100, layout.Clusters[0].Frame.Bottom + 4),
            "the cluster gap belongs to nobody");
    }

    public static void Test_FramePadding_HitsNothing()
    {
        var layout = Build(Fixtures.Ryzen7945HX, 400);
        var cluster = layout.Clusters[0];

        AssertNone(
            CardHitTest.At(layout, cluster.Frame.X + 5, cluster.Cells[0].Rect.Y + 14),
            "frame padding left of the first cell");
    }

    public static void Test_Header_HitsItsCluster()
    {
        var layout = Build(Fixtures.CoreUltra155H, 400);
        foreach (var cluster in layout.Clusters)
        {
            var hit = CardHitTest.At(
                layout, cluster.Header.X + cluster.Header.Width / 2, cluster.Header.Y + cluster.Header.Height / 2);
            Assert.Equal(HitKind.ClusterHeader, hit.Kind, "header hit");
            Assert.Equal(cluster.Index, hit.ClusterIndex, "correct cluster");
            Assert.Equal(0, hit.Threads.Count, "a header carries no threads");
        }
    }

    public static void Test_OutsideTheCard_HitsNothing()
    {
        var layout = Build(Fixtures.Ryzen7945HX, 400);

        AssertNone(CardHitTest.At(layout, -1, 50), "left of the card");
        AssertNone(CardHitTest.At(layout, 50, -1), "above the card");
        AssertNone(CardHitTest.At(layout, 50, layout.Height + 1), "below the last row");
        AssertNone(CardHitTest.At(layout, layout.Width + 1, 50), "right of the card");
    }

    public static void Test_EveryAreaAtLeast20By28()
    {
        foreach (var dump in AllDumps)
        {
            var layout = Build(dump, 400);
            foreach (var cluster in layout.Clusters)
            {
                Assert.True(cluster.Header.Height >= 28, $"header height ({dump})");
                foreach (var zone in cluster.Cells.SelectMany(c => c.Zones))
                {
                    if (zone.Threads.Count == 1)
                    {
                        Assert.True(zone.Rect.Width >= 20, $"slice width ({dump})");
                        Assert.True(zone.Rect.Height >= 28, $"slice height ({dump})");
                    }
                    else
                    {
                        Assert.Equal(4.0, zone.Rect.Width, $"a seam is exactly 4 wide ({dump})");
                        Assert.Equal(28.0, zone.Rect.Height, $"a seam is exactly 28 high ({dump})");
                    }
                }
            }
        }
    }

    private static string ThreadsAt(CardLayout layout, double x, double y)
        => string.Join(",", CardHitTest.At(layout, x, y).Threads);

    private static void AssertNone(HitZone hit, string because)
    {
        Assert.Equal(HitKind.None, hit.Kind, because);
        Assert.Equal(-1, hit.ClusterIndex, because + " (cluster index)");
        Assert.Equal(0, hit.Threads.Count, because + " (no threads)");
    }
}
