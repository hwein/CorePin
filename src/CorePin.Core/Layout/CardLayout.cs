namespace CorePin.Core.Layout;

/// All values in device-independent pixels; deliberately not a WPF type.
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    /// Half-open on both axes, so every point belongs to exactly one rectangle.
    public bool Contains(double px, double py) => px >= X && px < Right && py >= Y && py < Bottom;
}

/// One clickable area inside a cell: a single thread slice, or a seam taking both neighbours.
public sealed record CellZone(LayoutRect Rect, IReadOnlyList<int> Threads);

public sealed record CellBox(
    int ClusterIndex,
    int CoreIndex,
    LayoutRect Rect,
    IReadOnlyList<CellZone> Zones,      // slice, [seam, slice]* — ordered left to right
    IReadOnlyList<double> Dividers,     // x centers of the seams, for the divider stroke
    LayoutRect LabelRect,
    string Label,                       // "0·1" or "16"
    bool LabelVisible);                 // false = thinned out by the label stride

public sealed record ClusterBox(
    int Index,
    LayoutRect Frame,
    LayoutRect Header,                  // hit area of the header line
    string HeaderLabel,                 // "CCD 0"
    string HeaderFacts,                 // "V-Cache · 8C/16T · 96 MB L3"
    IReadOnlyList<CellBox> Cells,
    int RowCount);

public sealed record CardLayout(
    double Width,
    double Height,
    IReadOnlyList<ClusterBox> Clusters,
    CardMetrics Metrics);
