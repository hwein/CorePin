namespace CorePin.Core.Layout;

/// All measures in device-independent pixels; no defaults — every caller states every value.
public sealed record CardMetrics
{
    public required double SliceWidth { get; init; }
    public required double CellHeight { get; init; }
    public required double SeamWidth { get; init; }
    public required double CellGap { get; init; }
    public required double RowGap { get; init; }
    public required double LabelGap { get; init; }
    public required double LabelHeight { get; init; }   // measured line height, pre-rounded up to the 4-DIP grid by the caller
    public required double LabelAdvance { get; init; }  // per-character advance; feeds only the label stride, never a position
    public required double HeaderHeight { get; init; }
    public required double HeaderGap { get; init; }
    public required double FramePad { get; init; }
    public required double FrameBorder { get; init; }
    public required double ClusterGap { get; init; }
}
