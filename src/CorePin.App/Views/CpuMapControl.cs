using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CorePin.App.Themes;
using CorePin.Core.Layout;
using CorePin.Core.Primitives;
using CorePin.Core.Topology;

namespace CorePin.App.Views;

/// The drawn CPU map: clusters, cells, hover, keyboard cursor. Knows one mask, never a rule.
public sealed class CpuMapControl : FrameworkElement
{
    internal const string EmptyRuleNotice = "A rule needs at least one thread.";

    /// Card width used when no parent constrains us — a wiring error, not a supported case.
    private const double FallbackWidth = 398;

    private CpuTopology? _topology;
    private ThemeController? _theme;
    private CardMetrics? _metrics;
    private CardLayout? _layout;
    private double _layoutWidth = double.NaN;
    private List<(int Cluster, int Core, int Zone)>? _slices;

    private AffinityMask _selection;
    private bool _showSelection;
    private bool _interactive;

    private HitZone _hover = CardHitTest.Nothing;
    private HitZone _pressed = CardHitTest.Nothing;
    private CardCursor? _cursor;
    private double _preferredX;
    private bool _focusCueVisible;

    private BrushCache? _brushes;
    private TextCache? _texts;

    public CpuMapControl()
    {
        FocusVisualStyle = null;   // the control draws its own focus cue
        Loaded += (_, _) => { if (_theme is { } theme) theme.ThemeApplied += OnThemeApplied; };
        Unloaded += (_, _) => { if (_theme is { } theme) theme.ThemeApplied -= OnThemeApplied; };
    }

    /// A checked, never empty mask.
    public event Action<AffinityMask>? SelectionChanged;

    /// Footer message; null clears it.
    public event Action<string?>? Notice;

    internal AffinityMask Selection => _showSelection ? _selection : AffinityMask.Empty;

    internal bool ShowSelection => _showSelection;

    internal void Initialize(CpuTopology topology, ThemeController theme)
    {
        _topology = topology;
        _theme = theme;
        if (IsLoaded) theme.ThemeApplied += OnThemeApplied;
        InvalidateMeasure();
    }

    internal void SetInput(AffinityMask selection, bool showSelection, bool interactive)
    {
        _selection = selection;
        _showSelection = showSelection;
        _interactive = interactive;
        Focusable = interactive;
        if (!interactive) _hover = CardHitTest.Nothing;
        InvalidateVisual();
    }

    /// The one funnel for every gesture: reject empty, swallow no-ops, then announce.
    internal void ApplyCandidate(AffinityMask candidate)
    {
        if (!_interactive) return;
        if (candidate == _selection) return;
        if (candidate.IsEmpty)
        {
            Notice?.Invoke(EmptyRuleNotice);
            return;
        }

        _selection = candidate;
        _showSelection = true;   // a selection the user just made is always shown
        Notice?.Invoke(null);
        InvalidateVisual();
        SelectionChanged?.Invoke(candidate);
    }

    private void OnThemeApplied()
    {
        _brushes = null;
        _texts = null;
        InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _texts = null;   // pixelsPerDip is baked into every FormattedText; geometry is DIP and stays
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_topology is not { } topology) return default;

        _metrics ??= BuildMetrics();
        double width = double.IsInfinity(availableSize.Width) ? FallbackWidth : availableSize.Width;
        if (_layout is null || width != _layoutWidth)
        {
            _layout = CardLayoutBuilder.Build(topology, width, _metrics);
            _layoutWidth = width;
        }
        _slices ??= SliceIndex(_layout);
        return new Size(_layout.Width, _layout.Height);
    }

    /// All literal measures come from tokens; the two text measures are taken here, once.
    private CardMetrics BuildMetrics()
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var mono = new Typeface(Tokens.FontMono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var probe = new FormattedText("0·1", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            mono, Tokens.FontSizeMono, Brushes.Black, null, TextFormattingMode.Display, pixelsPerDip);
        var advance = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            mono, Tokens.FontSizeMono, Brushes.Black, null, TextFormattingMode.Display, pixelsPerDip);

        return new CardMetrics
        {
            SliceWidth = Tokens.HitTargetMinWidth,
            CellHeight = Tokens.HitTargetMinHeight,
            SeamWidth = Tokens.Spacing4,
            CellGap = Tokens.Spacing4,
            RowGap = Tokens.Spacing8,
            LabelGap = Tokens.Spacing4,
            LabelHeight = Math.Ceiling(probe.Height / 4.0) * 4.0,   // binding: measured, rounded up to the 4-DIP grid
            LabelAdvance = advance.WidthIncludingTrailingWhitespace,
            HeaderHeight = Tokens.HitTargetMinHeight,
            HeaderGap = Tokens.Spacing8,
            FramePad = Tokens.Spacing8,
            FrameBorder = 1.0,
            ClusterGap = Tokens.Spacing8,
        };
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_layout is not { } layout || _topology is not { } topology) return;

        var palette = Palette();
        var texts = Texts(layout, palette);
        var selection = Selection;
        double cellRadius = Tokens.RadiusCoreCell;
        double cardRadius = Tokens.RadiusCard;
        double dividerInset = Tokens.Spacing4;

        foreach (var cluster in layout.Clusters)
            DrawCluster(drawingContext, cluster, topology, texts, palette, selection,
                cellRadius, cardRadius, dividerInset);

        if (_interactive) DrawOverlays(drawingContext, layout, palette);
    }

    private static void DrawCluster(DrawingContext dc, ClusterBox cluster, CpuTopology topology, TextCache texts,
        BrushCache palette, AffinityMask selection, double cellRadius, double cardRadius, double dividerInset)
    {
        var headerBox = HeaderBoxRect(cluster);
        dc.PushGuidelineSet(Guidelines(cluster, headerBox));

        dc.DrawRoundedRectangle(palette.CardBackground, palette.BorderPen, ToRect(cluster.Frame),
            cardRadius, cardRadius);
        DrawHeader(dc, cluster, topology.Clusters[cluster.Index], headerBox, texts, palette, selection, cellRadius);
        foreach (var cell in cluster.Cells)
            DrawCell(dc, cell, texts.CellLabels[cluster.Index][cell.CoreIndex], palette, selection,
                cellRadius, dividerInset);

        dc.Pop();
    }

    /// One set per cluster: every stroked edge, each shifted by half the 1-DIP pen.
    private static GuidelineSet Guidelines(ClusterBox cluster, Rect headerBox)
    {
        var guidelines = new GuidelineSet();
        AddX(guidelines, cluster.Frame.X);
        AddX(guidelines, cluster.Frame.Right);
        AddY(guidelines, cluster.Frame.Y);
        AddY(guidelines, cluster.Frame.Bottom);
        AddX(guidelines, headerBox.X);
        AddX(guidelines, headerBox.Right);
        AddY(guidelines, headerBox.Y);
        AddY(guidelines, headerBox.Bottom);
        foreach (var cell in cluster.Cells)
        {
            AddX(guidelines, cell.Rect.X);
            AddX(guidelines, cell.Rect.Right);
            AddY(guidelines, cell.Rect.Y);
            AddY(guidelines, cell.Rect.Bottom);
            foreach (double divider in cell.Dividers) AddX(guidelines, divider);
        }
        guidelines.Freeze();
        return guidelines;
    }

    private static void AddX(GuidelineSet guidelines, double x) => guidelines.GuidelinesX.Add(x + 0.5);

    private static void AddY(GuidelineSet guidelines, double y) => guidelines.GuidelinesY.Add(y + 0.5);

    private static Rect HeaderBoxRect(ClusterBox cluster)
        => new(cluster.Header.X, cluster.Header.Y + Math.Floor((cluster.Header.Height - 14) / 2), 14, 14);

    private static void DrawHeader(DrawingContext dc, ClusterBox cluster, CpuCluster data, Rect box,
        TextCache texts, BrushCache palette, AffinityMask selection, double cellRadius)
    {
        int total = 0;
        int selected = 0;
        foreach (var core in data.Cores)
        {
            foreach (int thread in core.Threads)
            {
                total++;
                if (selection.Contains(thread)) selected++;
            }
        }

        if (selected == total)
        {
            dc.DrawRoundedRectangle(palette.Accent, null, box, cellRadius, cellRadius);
        }
        else
        {
            dc.DrawRoundedRectangle(null, palette.BorderPen, box, cellRadius, cellRadius);
            if (selected > 0)
                dc.DrawRectangle(palette.Accent, null, new Rect(box.X + 3, box.Y + 3, 8, 8));
        }

        double gap = Tokens.Spacing8;
        var header = cluster.Header;
        var label = texts.HeaderLabels[cluster.Index];
        double labelX = box.Right + gap;
        dc.DrawText(label, new Point(labelX, Math.Floor(header.Y + ((header.Height - label.Height) / 2))));

        var facts = texts.HeaderFacts[cluster.Index];
        if (facts.Text.Length == 0) return;
        double factsX = Math.Floor(labelX + label.WidthIncludingTrailingWhitespace + gap);
        double remaining = header.Right - factsX;
        if (remaining <= 0) return;
        facts.MaxTextWidth = remaining;
        dc.DrawText(facts, new Point(factsX, Math.Floor(header.Y + ((header.Height - facts.Height) / 2))));
    }

    private static void DrawCell(DrawingContext dc, CellBox cell, FormattedText label, BrushCache palette,
        AffinityMask selection, double cellRadius, double dividerInset)
    {
        var rect = cell.Rect;
        int threadCount = (cell.Zones.Count + 1) / 2;

        // A thread's fill runs to the middle of its seams; only the outer edges are rounded.
        for (int i = 0; i < threadCount; i++)
        {
            if (!selection.Contains(cell.Zones[2 * i].Threads[0])) continue;
            double left = i == 0 ? rect.X : cell.Dividers[i - 1];
            double right = i == threadCount - 1 ? rect.Right : cell.Dividers[i];
            dc.DrawGeometry(palette.Accent, null, Segment(
                new Rect(left, rect.Y, right - left, rect.Height),
                i == 0 ? cellRadius : 0,
                i == threadCount - 1 ? cellRadius : 0));
        }

        dc.DrawRoundedRectangle(null, palette.BorderPen, ToRect(rect), cellRadius, cellRadius);

        foreach (double divider in cell.Dividers)
            dc.DrawLine(palette.BorderPen,
                new Point(divider, rect.Y + dividerInset),
                new Point(divider, rect.Bottom - dividerInset));

        if (!cell.LabelVisible) return;
        double x = Math.Floor(cell.LabelRect.X + ((cell.LabelRect.Width - label.WidthIncludingTrailingWhitespace) / 2));
        dc.DrawText(label, new Point(x, cell.LabelRect.Y));
    }

    /// A rectangle whose left and right corners can be rounded independently.
    private static StreamGeometry Segment(Rect rect, double radiusLeft, double radiusRight)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var sizeLeft = new Size(radiusLeft, radiusLeft);
            var sizeRight = new Size(radiusRight, radiusRight);
            ctx.BeginFigure(new Point(rect.Left + radiusLeft, rect.Top), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(rect.Right - radiusRight, rect.Top), false, false);
            if (radiusRight > 0)
                ctx.ArcTo(new Point(rect.Right, rect.Top + radiusRight), sizeRight, 0, false,
                    SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(rect.Right, rect.Bottom - radiusRight), false, false);
            if (radiusRight > 0)
                ctx.ArcTo(new Point(rect.Right - radiusRight, rect.Bottom), sizeRight, 0, false,
                    SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(rect.Left + radiusLeft, rect.Bottom), false, false);
            if (radiusLeft > 0)
                ctx.ArcTo(new Point(rect.Left, rect.Bottom - radiusLeft), sizeLeft, 0, false,
                    SweepDirection.Clockwise, false, false);
            ctx.LineTo(new Point(rect.Left, rect.Top + radiusLeft), false, false);
            if (radiusLeft > 0)
                ctx.ArcTo(new Point(rect.Left + radiusLeft, rect.Top), sizeLeft, 0, false,
                    SweepDirection.Clockwise, false, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void DrawOverlays(DrawingContext dc, CardLayout layout, BrushCache palette)
    {
        if (_hover.Kind != HitKind.None && Outline(layout, _hover) is { } hovered)
            DrawCrisp(dc, palette.AccentPen, hovered);

        if (!IsKeyboardFocused) return;
        DrawCrisp(dc, palette.AccentPen, new LayoutRect(0.5, 0.5, layout.Width - 1, layout.Height - 1));
        if (_focusCueVisible && _cursor is { } cursor && CursorRect(layout, cursor) is { } area)
            DrawCrisp(dc, palette.FocusPen, new LayoutRect(area.X - 2, area.Y - 2, area.Width + 4, area.Height + 4));
    }

    private static void DrawCrisp(DrawingContext dc, Pen pen, LayoutRect rect)
    {
        var guidelines = new GuidelineSet();
        AddX(guidelines, rect.X);
        AddX(guidelines, rect.Right);
        AddY(guidelines, rect.Y);
        AddY(guidelines, rect.Bottom);
        guidelines.Freeze();
        dc.PushGuidelineSet(guidelines);
        dc.DrawRectangle(null, pen, ToRect(rect));
        dc.Pop();
    }

    /// The area a click on this zone would take: a slice, both seam neighbours, or the header.
    private static LayoutRect? Outline(CardLayout layout, HitZone hit)
    {
        if (hit.ClusterIndex < 0 || hit.ClusterIndex >= layout.Clusters.Count) return null;
        var cluster = layout.Clusters[hit.ClusterIndex];
        if (hit.Kind == HitKind.ClusterHeader) return cluster.Header;
        if (hit.Kind != HitKind.Threads) return null;

        foreach (var cell in cluster.Cells)
        {
            foreach (var zone in cell.Zones)
            {
                if (!SameThreads(zone.Threads, hit.Threads)) continue;
                if (zone.Threads.Count == 1) return zone.Rect;

                double left = zone.Rect.X;
                double right = zone.Rect.Right;
                foreach (var other in cell.Zones)
                {
                    if (other.Threads.Count != 1 || !hit.Threads.Contains(other.Threads[0])) continue;
                    left = Math.Min(left, other.Rect.X);
                    right = Math.Max(right, other.Rect.Right);
                }
                return new LayoutRect(left, cell.Rect.Y, right - left, cell.Rect.Height);
            }
        }
        return null;
    }

    private static LayoutRect? CursorRect(CardLayout layout, CardCursor cursor)
    {
        if (cursor.Cluster >= layout.Clusters.Count) return null;
        var cluster = layout.Clusters[cursor.Cluster];
        if (cursor.IsHeader) return cluster.Header;
        if (cursor.Core >= cluster.Cells.Count) return null;
        var zones = cluster.Cells[cursor.Core].Zones;
        return cursor.Zone < zones.Count ? zones[cursor.Zone].Rect : null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_interactive || _layout is not { } layout) return;

        var position = e.GetPosition(this);
        var zone = CardHitTest.At(layout, position.X, position.Y);
        if (SameZone(zone, _hover)) return;
        _hover = zone;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover.Kind == HitKind.None) return;
        _hover = CardHitTest.Nothing;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!_interactive || _layout is not { } layout) return;

        var position = e.GetPosition(this);
        _pressed = CardHitTest.At(layout, position.X, position.Y);
        _focusCueVisible = false;
        CaptureMouse();
        Focus();
        e.Handled = true;
        InvalidateVisual();
    }

    /// The effect fires on release, and only if press and release hit the same zone.
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();

        var pressed = _pressed;
        _pressed = CardHitTest.Nothing;
        if (!_interactive || _layout is not { } layout || _topology is not { } topology) return;

        var position = e.GetPosition(this);
        var zone = CardHitTest.At(layout, position.X, position.Y);
        if (zone.Kind == HitKind.None || !SameZone(zone, pressed)) return;

        e.Handled = true;
        MoveCursorTo(layout, zone);
        if (zone.Kind == HitKind.Threads)
            ApplyCandidate(SelectionOps.ToggleThreads(_selection, zone.Threads));
        else
            ApplyCandidate(SelectionOps.ToggleCluster(_selection, topology.Clusters[zone.ClusterIndex]));
    }

    /// The mouse continues the keyboard: a click parks the cursor on the clicked area.
    private void MoveCursorTo(CardLayout layout, HitZone zone)
    {
        if (zone.Kind == HitKind.ClusterHeader)
        {
            _cursor = new CardCursor(zone.ClusterIndex, 0, 0, true);
            return;
        }

        var cluster = layout.Clusters[zone.ClusterIndex];
        for (int core = 0; core < cluster.Cells.Count; core++)
        {
            var zones = cluster.Cells[core].Zones;
            for (int index = 0; index < zones.Count; index++)
            {
                if (!ReferenceEquals(zones[index].Threads, zone.Threads)) continue;
                int slice = zones[index].Threads.Count == 1 ? index : index - 1;   // a seam has no key
                _cursor = new CardCursor(zone.ClusterIndex, core, slice, false);
                var rect = zones[slice].Rect;
                _preferredX = rect.X + (rect.Width / 2);
                return;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_interactive || _layout is not { } layout || _topology is not { } topology)
        {
            base.OnKeyDown(e);
            return;
        }

        bool cueChanged = !_focusCueVisible;
        _focusCueVisible = true;
        bool handled = true;
        switch (e.Key)
        {
            case Key.Left: MoveHorizontal(layout, -1); break;
            case Key.Right: MoveHorizontal(layout, +1); break;
            case Key.Up: MoveVertical(layout, -1); break;
            case Key.Down: MoveVertical(layout, +1); break;
            case Key.Home: MoveToSlice(layout, 0); break;
            case Key.End: MoveToSlice(layout, (_slices ??= SliceIndex(layout)).Count - 1); break;
            case Key.Space:
            case Key.Enter: ToggleAtCursor(layout, topology); break;
            case Key.A when Keyboard.Modifiers == ModifierKeys.Control:
                ApplyCandidate(topology.MachineMask);
                break;
            default: handled = false; break;
        }

        if (handled) e.Handled = true;   // the arrows must never reach the rule list
        else base.OnKeyDown(e);
        if (handled || cueChanged) InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        if (_cursor is null && _layout is { } layout) MoveToSlice(layout, 0, reveal: false);
        InvalidateVisual();
        base.OnGotKeyboardFocus(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        InvalidateVisual();
        base.OnLostKeyboardFocus(e);
    }

    private void MoveHorizontal(CardLayout layout, int delta)
    {
        if (_cursor is not { IsHeader: false } cursor) return;
        var slices = _slices ??= SliceIndex(layout);
        int index = slices.IndexOf((cursor.Cluster, cursor.Core, cursor.Zone));
        if (index < 0) return;
        MoveToSlice(layout, index + delta);   // no wrap: out-of-range indexes do nothing
    }

    private void MoveToSlice(CardLayout layout, int index, bool reveal = true)
    {
        var slices = _slices ??= SliceIndex(layout);
        if (index < 0 || index >= slices.Count) return;
        var (cluster, core, zone) = slices[index];
        _cursor = new CardCursor(cluster, core, zone, false);
        var rect = layout.Clusters[cluster].Cells[core].Zones[zone].Rect;
        _preferredX = rect.X + (rect.Width / 2);
        if (reveal) BringCursorIntoView(layout);
    }

    /// Rows walk through the header: above a cluster's first row sits its header line.
    private void MoveVertical(CardLayout layout, int delta)
    {
        if (_cursor is not { } cursor) return;
        var cluster = layout.Clusters[cursor.Cluster];

        if (cursor.IsHeader)
        {
            if (delta > 0)
            {
                MoveToNearestInRow(layout, cursor.Cluster, RowTops(cluster)[0]);
            }
            else if (cursor.Cluster > 0)
            {
                var previous = layout.Clusters[cursor.Cluster - 1];
                MoveToNearestInRow(layout, cursor.Cluster - 1, RowTops(previous)[^1]);
            }
            return;
        }

        var tops = RowTops(cluster);
        int row = tops.IndexOf(cluster.Cells[cursor.Core].Rect.Y);
        int target = row + delta;
        if (target >= 0 && target < tops.Count)
        {
            MoveToNearestInRow(layout, cursor.Cluster, tops[target]);
        }
        else if (target < 0)
        {
            _cursor = new CardCursor(cursor.Cluster, 0, 0, true);
            BringCursorIntoView(layout);
        }
        else if (cursor.Cluster + 1 < layout.Clusters.Count)
        {
            _cursor = new CardCursor(cursor.Cluster + 1, 0, 0, true);
            BringCursorIntoView(layout);
        }
    }

    /// Distinct cell top edges, ascending; cells arrive in row-major order.
    private static List<double> RowTops(ClusterBox cluster)
    {
        var tops = new List<double>();
        foreach (var cell in cluster.Cells)
        {
            if (tops.Count == 0 || tops[^1] != cell.Rect.Y) tops.Add(cell.Rect.Y);
        }
        return tops;
    }

    private void MoveToNearestInRow(CardLayout layout, int clusterIndex, double rowY)
    {
        var cluster = layout.Clusters[clusterIndex];
        int bestCore = -1;
        int bestZone = -1;
        double bestDistance = double.MaxValue;
        for (int core = 0; core < cluster.Cells.Count; core++)
        {
            var cell = cluster.Cells[core];
            if (cell.Rect.Y != rowY) continue;
            for (int zone = 0; zone < cell.Zones.Count; zone++)
            {
                if (cell.Zones[zone].Threads.Count != 1) continue;
                var rect = cell.Zones[zone].Rect;
                double distance = Math.Abs(rect.X + (rect.Width / 2) - _preferredX);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestCore = core;
                bestZone = zone;
            }
        }
        if (bestCore < 0) return;
        _cursor = new CardCursor(clusterIndex, bestCore, bestZone, false);
        BringCursorIntoView(layout);
    }

    private void ToggleAtCursor(CardLayout layout, CpuTopology topology)
    {
        if (_cursor is not { } cursor) return;
        if (cursor.IsHeader)
        {
            ApplyCandidate(SelectionOps.ToggleCluster(_selection, topology.Clusters[cursor.Cluster]));
            return;
        }
        var zone = layout.Clusters[cursor.Cluster].Cells[cursor.Core].Zones[cursor.Zone];
        ApplyCandidate(SelectionOps.ToggleThreads(_selection, zone.Threads));
    }

    private void BringCursorIntoView(CardLayout layout)
    {
        if (_cursor is not { } cursor || CursorRect(layout, cursor) is not { } rect) return;
        double margin = Tokens.Spacing4;
        BringIntoView(new Rect(
            rect.X - margin, rect.Y - margin, rect.Width + (2 * margin), rect.Height + (2 * margin)));
    }

    /// Slice zones in reading order; indices stay valid because the topology never changes.
    private static List<(int Cluster, int Core, int Zone)> SliceIndex(CardLayout layout)
    {
        var slices = new List<(int, int, int)>();
        for (int cluster = 0; cluster < layout.Clusters.Count; cluster++)
        {
            var cells = layout.Clusters[cluster].Cells;
            for (int core = 0; core < cells.Count; core++)
            {
                for (int zone = 0; zone < cells[core].Zones.Count; zone++)
                {
                    if (cells[core].Zones[zone].Threads.Count == 1) slices.Add((cluster, core, zone));
                }
            }
        }
        return slices;
    }

    private BrushCache Palette()
    {
        if (_brushes is { } cached) return cached;

        var accent = Frozen(Tokens.Accent);
        var primary = Frozen(Tokens.TextPrimary);
        var borderPen = new Pen(Frozen(Tokens.Border), 1);
        borderPen.Freeze();
        var accentPen = new Pen(accent, 1);
        accentPen.Freeze();
        var focusPen = new Pen(primary, 1);
        focusPen.Freeze();
        return _brushes = new BrushCache(
            Frozen(Tokens.CardBackground), accent, primary, Frozen(Tokens.TextSecondary),
            borderPen, accentPen, focusPen);
    }

    private TextCache Texts(CardLayout layout, BrushCache palette)
    {
        if (_texts is { } cached) return cached;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var heading = new Typeface(Tokens.FontHeading, FontStyles.Normal, Tokens.FontWeightHeading,
            FontStretches.Normal);
        var ui = new Typeface(Tokens.FontUI, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var mono = new Typeface(Tokens.FontMono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        var labels = new FormattedText[layout.Clusters.Count];
        var facts = new FormattedText[layout.Clusters.Count];
        var cells = new FormattedText[layout.Clusters.Count][];
        for (int i = 0; i < layout.Clusters.Count; i++)
        {
            var cluster = layout.Clusters[i];
            labels[i] = Make(cluster.HeaderLabel, heading, Tokens.FontSizeHeading, palette.TextPrimary, pixelsPerDip);
            facts[i] = Make(cluster.HeaderFacts, ui, Tokens.FontSizeUI, palette.TextSecondary, pixelsPerDip);
            facts[i].Trimming = TextTrimming.CharacterEllipsis;   // only the facts may shorten, never the label
            facts[i].MaxLineCount = 1;
            cells[i] = new FormattedText[cluster.Cells.Count];
            for (int j = 0; j < cluster.Cells.Count; j++)
                cells[i][j] = Make(cluster.Cells[j].Label, mono, Tokens.FontSizeMono, palette.TextSecondary,
                    pixelsPerDip);
        }
        return _texts = new TextCache(labels, facts, cells);
    }

    private static FormattedText Make(string text, Typeface typeface, double size, Brush brush, double pixelsPerDip)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush,
            null, TextFormattingMode.Display, pixelsPerDip);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// Record equality compares the thread lists by reference; two hits must match by content.
    private static bool SameZone(HitZone a, HitZone b)
        => a.Kind == b.Kind && a.ClusterIndex == b.ClusterIndex && SameThreads(a.Threads, b.Threads);

    /// Reference equality is the fast path; a layout rebuilt after a resize needs a content compare.
    private static bool SameThreads(IReadOnlyList<int> a, IReadOnlyList<int> b)
        => ReferenceEquals(a, b) || a.SequenceEqual(b);

    private static Rect ToRect(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private readonly record struct CardCursor(int Cluster, int Core, int Zone, bool IsHeader);

    private sealed record BrushCache(
        Brush CardBackground, Brush Accent, Brush TextPrimary, Brush TextSecondary,
        Pen BorderPen, Pen AccentPen, Pen FocusPen);

    private sealed record TextCache(
        FormattedText[] HeaderLabels, FormattedText[] HeaderFacts, FormattedText[][] CellLabels);
}
