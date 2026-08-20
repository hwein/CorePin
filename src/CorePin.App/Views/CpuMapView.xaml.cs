using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CorePin.App.Themes;
using CorePin.Core.Layout;
using CorePin.Core.Primitives;
using CorePin.Core.Topology;
using CorePin.Core.ViewModel;

namespace CorePin.App.Views;

/// Host around the drawn map: presets, SMT toggle, footer, rule switch. It knows the rule id.
public partial class CpuMapView : UserControl
{
    private const string ClipboardBusyNotice = "Couldn't copy — the clipboard is busy.";
    private const string TopologyCopiedNotice = "Topology copied — paste it into a new GitHub issue.";
    private const string TopologyCopiedOpenedNotice = "Topology copied — finish the issue in your browser.";
    private const string CopyMaskTooltip = "Copy the affinity mask";

    private readonly List<Button> _presetButtons = [];
    private CpuTopology? _topology;
    private Func<string, bool>? _copyText;
    private Func<bool>? _openIssuePage;
    private Guid? _ruleId;
    private CpuCardInput? _input;
    private string? _lockedTooltip;
    private string? _notice;

    public CpuMapView()
    {
        InitializeComponent();
    }

    public event Action<Guid, AffinityMask>? SelectionChanged;

    public event Action<Guid, bool>? RuleEnabledChanged;

    public void Initialize(CpuTopology topology, ThemeController theme, Func<string, bool> copyText,
                            Func<bool> openIssuePage)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(copyText);
        ArgumentNullException.ThrowIfNull(openIssuePage);

        _topology = topology;
        _copyText = copyText;
        _openIssuePage = openIssuePage;
        Map.Initialize(topology, theme);
        Map.SelectionChanged += OnMapSelectionChanged;
        Map.Notice += OnMapNotice;

        if (topology.Profiling == ProfilingLevel.NotProfiled) HintBlock.Visibility = Visibility.Visible;
        SmtToggle.Visibility = topology.HasSmt ? Visibility.Visible : Visibility.Collapsed;

        int index = 0;
        foreach (var (label, mask) in SelectionOps.Presets(topology))
        {
            var button = new Button
            {
                Content = label,
                Padding = new Thickness(Tokens.Spacing8, 0, Tokens.Spacing8, 0),
                Margin = new Thickness(0, 4, 8, 0),
                MinWidth = Tokens.HitTargetMinWidth,
                Height = Tokens.HitTargetMinHeight,
            };
            ToolTipService.SetShowOnDisabled(button, true);
            var presetMask = mask;   // one mask per closure, replacing the whole selection
            button.Click += (_, _) => Map.ApplyCandidate(presetMask);
            _presetButtons.Add(button);
            PresetRow.Children.Insert(index++, button);
        }

        SetRule(null, null, null);
    }

    /// The whole card state for one rule; null input means no rule is selected.
    public void SetRule(Guid? ruleId, CpuCardInput? input, string? lockedTooltip)
    {
        _ruleId = ruleId;
        _input = input;
        _lockedTooltip = lockedTooltip;
        _notice = null;   // a rule switch rebuilds the footer from scratch
        Map.SetInput(
            input?.Threads ?? AffinityMask.Empty,
            input?.ShowSelection ?? false,
            input is { CanEditRules: true });
        Refresh();
    }

    private void OnMapSelectionChanged(AffinityMask mask)
    {
        if (_ruleId is { } id) SelectionChanged?.Invoke(id, mask);
        Refresh();
    }

    private void OnMapNotice(string? text)
    {
        _notice = text;
        Refresh();
    }

    private void MaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (_copyText is not { } copy) return;
        if (copy(Map.Selection.ToHex())) return;
        _notice = ClipboardBusyNotice;
        Refresh();
    }

    /// No browser opens unless the copy succeeds first — an empty clipboard would invite an empty issue.
    private void HelpNameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topology is not { } topology || _copyText is not { } copy || _openIssuePage is not { } openIssuePage)
            return;

        if (!copy(TopologyJson.Serialize(topology.Source)))
        {
            _notice = ClipboardBusyNotice;
            Refresh();
            return;
        }

        _notice = openIssuePage() ? TopologyCopiedOpenedNotice : TopologyCopiedNotice;
        Refresh();
    }

    /// The toggle is derived state: compute the direction first, then snap back to derived.
    private void SmtToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_topology is not { } topology) return;
        var selection = Map.Selection;
        Map.ApplyCandidate(SelectionOps.SmtIsOn(selection, topology)
            ? SelectionOps.WithoutSmt(selection, topology)
            : SelectionOps.WithSmt(selection, topology));
        Refresh();
    }

    private void RuleEnabledToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_ruleId is { } id) RuleEnabledChanged?.Invoke(id, RuleEnabledToggle.IsChecked == true);
    }

    private void Refresh()
    {
        if (_topology is not { } topology) return;

        bool hasRule = _input is not null;
        bool editable = _input is { CanEditRules: true };
        var selection = Map.Selection;

        foreach (var button in _presetButtons)
        {
            button.IsEnabled = hasRule && editable;
            button.ToolTip = _lockedTooltip;
        }

        SmtToggle.IsChecked = SelectionOps.SmtIsOn(selection, topology);
        SmtToggle.IsEnabled = hasRule && editable && SelectionOps.SmtIsEnabled(selection, topology);
        SmtToggle.ToolTip = _lockedTooltip;

        RuleEnabledToggle.IsChecked = _input?.RuleEnabled ?? false;
        RuleEnabledToggle.IsEnabled = hasRule && editable;
        RuleEnabledToggle.ToolTip = _lockedTooltip;

        UpdateFooter(topology, selection, hasRule, editable);
    }

    private void UpdateFooter(CpuTopology topology, AffinityMask selection, bool hasRule, bool editable)
    {
        if (_notice is { } notice)
        {
            NoticeText.Text = notice;
            NoticeText.Visibility = Visibility.Visible;
            FooterCounts.Visibility = Visibility.Collapsed;
            return;
        }
        NoticeText.Visibility = Visibility.Collapsed;
        FooterCounts.Visibility = Visibility.Visible;

        CountText.Text = string.Create(CultureInfo.InvariantCulture,
            $"{selection.Count} of {topology.LogicalProcessorCount} threads");

        bool maskVisible = hasRule && Map.ShowSelection;
        MaskSeparator.Visibility = maskVisible ? Visibility.Visible : Visibility.Collapsed;
        MaskButton.Visibility = maskVisible ? Visibility.Visible : Visibility.Collapsed;
        MaskButton.Content = selection.ToHex();
        MaskButton.IsEnabled = editable;
        MaskButton.ToolTip = editable ? CopyMaskTooltip : _lockedTooltip;
    }
}
