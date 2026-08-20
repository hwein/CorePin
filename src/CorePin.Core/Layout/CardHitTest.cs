namespace CorePin.Core.Layout;

public enum HitKind { None, Threads, ClusterHeader }

public readonly record struct HitZone(
    HitKind Kind,
    int ClusterIndex,                   // -1 for None
    IReadOnlyList<int> Threads);        // empty except for Threads

public static class CardHitTest
{
    /// The one value callers use for "no zone" — default(HitZone) has a null thread list.
    public static readonly HitZone Nothing = new(HitKind.None, -1, []);

    /// A miss is a miss — no snapping to the nearest cell.
    public static HitZone At(CardLayout layout, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(layout);

        foreach (var cluster in layout.Clusters)
        {
            if (!cluster.Frame.Contains(x, y)) continue;
            if (cluster.Header.Contains(x, y))
                return new HitZone(HitKind.ClusterHeader, cluster.Index, []);

            foreach (var cell in cluster.Cells)
            {
                if (!cell.Rect.Contains(x, y)) continue;
                foreach (var zone in cell.Zones)
                {
                    if (zone.Rect.Contains(x, y))
                        return new HitZone(HitKind.Threads, cluster.Index, zone.Threads);
                }
            }
            return Nothing;
        }
        return Nothing;
    }
}
