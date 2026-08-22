using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using CorePin.Core.Layout;
using CorePin.Core.Primitives;
using CorePin.Core.Topology;

namespace CorePin.App.Views;

/// The map as a multi-select list: one CheckBox per cluster header, one ListItem per thread.
internal sealed class CpuMapAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    private List<AutomationPeer>? _children;
    private CpuTopology? _childrenTopology;

    public CpuMapAutomationPeer(CpuMapControl owner) : base(owner) => Owner = owner;

    internal new CpuMapControl Owner { get; }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => "CpuMapControl";

    public override object? GetPattern(PatternInterface patternInterface)
        => patternInterface == PatternInterface.Selection ? this : base.GetPattern(patternInterface);

    /// Cached so every thread keeps one peer, and with it one runtime id, for as long as the map lives.
    protected override List<AutomationPeer> GetChildrenCore()
    {
        if (Owner.Topology is not { } topology) return [];
        if (_children is null || !ReferenceEquals(topology, _childrenTopology))
        {
            _children = BuildChildren(topology);
            _childrenTopology = topology;
        }
        return _children;
    }

    private List<AutomationPeer> BuildChildren(CpuTopology topology)
    {
        var children = new List<AutomationPeer>();
        for (int i = 0; i < topology.Clusters.Count; i++)
        {
            var cluster = topology.Clusters[i];
            children.Add(new CpuMapClusterPeer(this, i, cluster));
            foreach (var core in cluster.Cores)
            {
                foreach (int thread in core.Threads)
                    children.Add(new CpuMapThreadPeer(this, cluster, thread, sharesCore: core.Threads.Count > 1));
            }
        }
        return children;
    }

    bool ISelectionProvider.CanSelectMultiple => true;

    bool ISelectionProvider.IsSelectionRequired => false;

    IRawElementProviderSimple[] ISelectionProvider.GetSelection()
        => [.. GetChildren().OfType<CpuMapThreadPeer>().Where(thread => thread.IsSelected).Select(ProviderFromPeer)];

    internal void OnStateChanged(AffinityMask before, AffinityMask after, bool enabledChanged)
    {
        if (_children is null || !ListenerExists(AutomationEvents.PropertyChanged)) return;

        bool enabled = Owner.IsInteractive;
        foreach (var child in _children.Cast<CpuMapItemPeer>())
        {
            child.OnSelectionChanged(before, after);
            if (enabledChanged)
                child.RaisePropertyChangedEvent(AutomationElementIdentifiers.IsEnabledProperty, !enabled, enabled);
        }
    }

    /// The slice drawn for one thread and the cell around it; null before the first layout.
    internal (CellBox Cell, CellZone Slice)? SliceOf(int thread)
    {
        if (Owner.Layout is not { } layout) return null;
        foreach (var cluster in layout.Clusters)
        {
            foreach (var cell in cluster.Cells)
            {
                foreach (var zone in cell.Zones)
                {
                    if (zone.Threads.Count == 1 && zone.Threads[0] == thread) return (cell, zone);
                }
            }
        }
        return null;
    }

    internal Rect ThreadRect(int thread)
        => SliceOf(thread) is { } slice ? ToScreen(slice.Slice.Rect) : Rect.Empty;

    internal Rect ClusterHeaderRect(int clusterIndex)
        => Owner.Layout is { } layout ? ToScreen(layout.Clusters[clusterIndex].Header) : Rect.Empty;

    private Rect ToScreen(LayoutRect rect)
    {
        if (!Owner.IsVisible || PresentationSource.FromVisual(Owner) is null) return Rect.Empty;
        return new Rect(
            Owner.PointToScreen(new Point(rect.X, rect.Y)),
            Owner.PointToScreen(new Point(rect.Right, rect.Bottom)));
    }
}

/// A child the map draws itself: no element of its own, so the peer answers everything.
internal abstract class CpuMapItemPeer : AutomationPeer
{
    protected CpuMapItemPeer(CpuMapAutomationPeer parent) => Parent = parent;

    protected CpuMapAutomationPeer Parent { get; }

    internal abstract void OnSelectionChanged(AffinityMask before, AffinityMask after);

    /// Assistive technology takes the same funnel as a click; a disabled map refuses instead of ignoring.
    protected void Apply(AffinityMask candidate)
    {
        if (!Parent.Owner.IsInteractive) throw new ElementNotEnabledException();
        Parent.Owner.ApplyCandidate(candidate);
    }

    protected override Point GetClickablePointCore()
    {
        var rect = GetBoundingRectangleCore();
        return rect.IsEmpty
            ? new Point(double.NaN, double.NaN)
            : new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
    }

    protected override bool IsEnabledCore() => Parent.Owner.IsInteractive;

    protected override bool IsOffscreenCore() => !Parent.Owner.IsVisible;

    protected override void SetFocusCore() => Parent.Owner.Focus();

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override bool HasKeyboardFocusCore() => false;

    protected override bool IsPasswordCore() => false;

    protected override bool IsRequiredForFormCore() => false;

    protected override string GetAcceleratorKeyCore() => "";

    protected override string GetAccessKeyCore() => "";

    protected override string GetHelpTextCore() => "";

    protected override string GetItemStatusCore() => "";

    protected override string GetItemTypeCore() => "";

    protected override AutomationPeer? GetLabeledByCore() => null;

    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;

    protected override List<AutomationPeer>? GetChildrenCore() => null;
}

internal sealed class CpuMapThreadPeer : CpuMapItemPeer, ISelectionItemProvider
{
    private readonly CpuCluster _cluster;
    private readonly int _thread;
    private readonly bool _sharesCore;

    public CpuMapThreadPeer(CpuMapAutomationPeer parent, CpuCluster cluster, int thread, bool sharesCore)
        : base(parent)
    {
        _cluster = cluster;
        _thread = thread;
        _sharesCore = sharesCore;
    }

    internal bool IsSelected => Parent.Owner.Selection.Contains(_thread);

    /// The core label names the SMT sibling a listener cannot see sitting next to this slice.
    protected override string GetNameCore()
        => _sharesCore && Parent.SliceOf(_thread) is { } slice
            ? string.Create(CultureInfo.InvariantCulture, $"Thread {_thread}, core {slice.Cell.Label}, {_cluster.Label}")
            : string.Create(CultureInfo.InvariantCulture, $"Thread {_thread}, {_cluster.Label}");

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => "CpuMapThread";

    protected override string GetAutomationIdCore() => string.Create(CultureInfo.InvariantCulture, $"thread-{_thread}");

    protected override Rect GetBoundingRectangleCore() => Parent.ThreadRect(_thread);

    public override object? GetPattern(PatternInterface patternInterface)
        => patternInterface == PatternInterface.SelectionItem ? this : null;

    internal override void OnSelectionChanged(AffinityMask before, AffinityMask after)
    {
        bool wasSelected = before.Contains(_thread);
        bool isSelected = after.Contains(_thread);
        if (wasSelected != isSelected)
            RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, wasSelected, isSelected);
    }

    bool ISelectionItemProvider.IsSelected => IsSelected;

    IRawElementProviderSimple ISelectionItemProvider.SelectionContainer => ProviderFromPeer(Parent);

    void ISelectionItemProvider.Select() => Apply(AffinityMask.FromThreads([_thread]));

    void ISelectionItemProvider.AddToSelection() => Apply(Parent.Owner.EditMask.With(_thread));

    void ISelectionItemProvider.RemoveFromSelection() => Apply(Parent.Owner.EditMask.Without(_thread));
}

internal sealed class CpuMapClusterPeer : CpuMapItemPeer, IToggleProvider
{
    private readonly int _index;
    private readonly CpuCluster _cluster;

    public CpuMapClusterPeer(CpuMapAutomationPeer parent, int clusterIndex, CpuCluster cluster) : base(parent)
    {
        _index = clusterIndex;
        _cluster = cluster;
    }

    /// Complete → On, untouched → Off, anything between → Indeterminate, like the drawn header box.
    internal static ToggleState StateOf(AffinityMask mask, CpuCluster cluster)
    {
        int selected = cluster.Cores.Sum(core => core.Threads.Count(mask.Contains));
        if (selected == 0) return ToggleState.Off;
        return selected == cluster.LogicalCount ? ToggleState.On : ToggleState.Indeterminate;
    }

    protected override string GetNameCore()
        => _cluster.Badge is { } badge ? _cluster.Label + ", " + badge : _cluster.Label;

    protected override string GetHelpTextCore() => Parent.Owner.Layout?.Clusters[_index].HeaderFacts ?? "";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;

    protected override string GetClassNameCore() => "CpuMapCluster";

    protected override string GetAutomationIdCore() => string.Create(CultureInfo.InvariantCulture, $"cluster-{_index}");

    protected override Rect GetBoundingRectangleCore() => Parent.ClusterHeaderRect(_index);

    public override object? GetPattern(PatternInterface patternInterface)
        => patternInterface == PatternInterface.Toggle ? this : null;

    internal override void OnSelectionChanged(AffinityMask before, AffinityMask after)
    {
        var oldState = StateOf(before, _cluster);
        var newState = StateOf(after, _cluster);
        if (oldState != newState)
            RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, oldState, newState);
    }

    ToggleState IToggleProvider.ToggleState => StateOf(Parent.Owner.Selection, _cluster);

    void IToggleProvider.Toggle() => Apply(SelectionOps.ToggleCluster(Parent.Owner.EditMask, _cluster));
}
