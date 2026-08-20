using System.Collections.ObjectModel;
using System.ComponentModel;
using CorePin.Core.Configuration;
using CorePin.Core.Diagnostics;
using CorePin.Core.Engine;
using CorePin.Core.Primitives;
using CorePin.Core.Rules;

namespace CorePin.Core.ViewModel;

/// Owns the authoritative rule set; the engine only ever gets a copy. UI thread only.
public sealed class RuleListViewModel : INotifyPropertyChanged
{
    public const string EmptyListText = "No rules yet — add an app to get started.";

    private readonly ObservableCollection<RuleRow> _rows = [];
    private readonly ReadOnlyObservableCollection<RuleRow> _rowsReadOnly;
    private readonly Dictionary<Guid, RuleRow> _rowsById = [];
    private readonly WriteGuard _guard;
    private readonly AffinityMask _machineMask;
    private readonly int _schemaVersion;
    private readonly MachineInfo _loadedMachine;
    private readonly MachineInfo _measuredMachine;
    private readonly Settings _settings;
    private readonly string? _loadBanner;
    private readonly string? _skippedBanner;
    private readonly Action<RuleSet, RuleChange> _submit;
    private readonly Action<AppConfig> _requestSave;
    private readonly Func<AffinityMask, string> _describeSelection;
    private readonly ILog _log;

    private RuleSet _rules;
    private Guid? _selectedRuleId;
    private Guid? _pendingDelete;
    private bool _faulted;
    private string? _lastAppliedExe;
    private DateTime? _lastAppliedUtc;
    private DateTime _lastTickUtc;
    private WindowBounds? _currentWindowBounds;

    public RuleListViewModel(
        RuleSet rules,
        WriteGuard guard,
        AffinityMask machineMask,
        int schemaVersion,
        MachineInfo loadedMachine,
        MachineInfo measuredMachine,
        Settings settings,
        ConfigLoadOutcome outcome,
        string? detail,
        int rulesSkipped,
        bool isElevated,
        Action<RuleSet, RuleChange> submit,
        Action<AppConfig> requestSave,
        Func<AffinityMask, string> describeSelection,
        ILog log)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(loadedMachine);
        ArgumentNullException.ThrowIfNull(measuredMachine);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(submit);
        ArgumentNullException.ThrowIfNull(requestSave);
        ArgumentNullException.ThrowIfNull(describeSelection);
        ArgumentNullException.ThrowIfNull(log);

        _rules = rules;
        _guard = guard;
        _machineMask = machineMask;
        _schemaVersion = schemaVersion;
        _loadedMachine = loadedMachine;
        _measuredMachine = measuredMachine;
        _settings = settings;
        _currentWindowBounds = settings.WindowBounds;
        _submit = submit;
        _requestSave = requestSave;
        _describeSelection = describeSelection;
        _log = log;
        IsElevated = isElevated;
        _rowsReadOnly = new ReadOnlyObservableCollection<RuleRow>(_rows);

        _loadBanner = LoadBanner(guard.Reason, outcome, detail);
        _skippedBanner = rulesSkipped > 0
            ? BannerText.RulesSkipped(rulesSkipped, detail)
            : null;

        foreach (var rule in rules.Rules.OrderBy(r => r.ExeName, StringComparer.OrdinalIgnoreCase))
            AddRow(rule);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// Sorted by exe name, OrdinalIgnoreCase; a state change never re-sorts.
    public ReadOnlyObservableCollection<RuleRow> Rows => _rowsReadOnly;

    public bool IsElevated { get; }

    public bool IsEditable => _guard.CanEditRules && !_faulted;

    public bool HasRules => _rows.Count > 0;

    public int TotalRules => _rows.Count;

    public int AppliedRules => _rows.Count(r => r.State == RuleState.Applied);

    public bool HasPendingDeleteConfirmation => _pendingDelete is not null;

    public WindowBounds? CurrentWindowBounds => _currentWindowBounds;

    /// What a locked button, row or card shows on hover; null while editing is allowed.
    public string? LockedTooltip
    {
        get
        {
            if (_faulted) return BannerText.EditingStopped;
            if (_guard.CanEditRules) return null;

            return _guard.Reason switch
            {
                GuardReason.ConfigTooNew => BannerText.ConfigTooNew,
                GuardReason.ConfigUnreadable => BannerText.ConfigUnreadable,
                _ => null,
            };
        }
    }

    /// Belongs into the status line before the shield, and only into DEBUG builds.
    public string? DebugTopologyHint
        => _guard.Reason == GuardReason.DebugTopology ? StatusLine.DebugTopologyHint : null;

    public IReadOnlyList<string> Banners
    {
        get
        {
            var banners = new List<string>(3);
            if (_rules.Rules.Any(r => r.NeedsReview)) banners.Add(BannerText.CpuChanged);
            if (_skippedBanner is not null) banners.Add(_skippedBanner);
            if (_loadBanner is not null) banners.Add(_loadBanner);
            return banners;
        }
    }

    public string StatusText
    {
        get
        {
            if (_faulted) return StatusLine.Stopped;

            bool anyApplied = _rows.Any(r => r.State == RuleState.Applied);
            if (!anyApplied || _lastAppliedExe is null || _lastAppliedUtc is not { } appliedUtc)
                return StatusLine.Compose(_rows.Count, null, null);

            return StatusLine.Compose(_rows.Count, _lastAppliedExe, _lastTickUtc - appliedUtc);
        }
    }

    public Guid? SelectedRuleId
    {
        get => _selectedRuleId;
        set
        {
            if (value is { } id && _rules.ById(id) is null) value = null;
            if (_selectedRuleId == value) return;

            _selectedRuleId = value;
            SetPendingDelete(null);
            Raise(nameof(SelectedRuleId));
            Raise(nameof(SelectedInput));
        }
    }

    public CpuCardInput? SelectedInput
    {
        get
        {
            if (_selectedRuleId is not { } id) return null;
            if (_rules.ById(id) is not { } rule) return null;

            bool showSelection = !rule.NeedsReview && RuleEvaluation.Check(rule, _machineMask) != RuleCheck.Invalid;
            return new CpuCardInput(
                showSelection ? rule.Threads : AffinityMask.Empty, showSelection, IsEditable, rule.Enabled);
        }
    }

    /// A partial list only updates the rules it names; unknown ids are dropped, not added.
    public void ApplyStatus(IReadOnlyList<RuleStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        bool changed = false;
        foreach (var status in statuses)
        {
            if (!_rowsById.TryGetValue(status.RuleId, out var row)) continue;

            if (row.Apply(status)) changed = true;
        }

        if (!changed) return;

        Raise(nameof(AppliedRules));
        Raise(nameof(StatusText));
    }

    public void ApplyHeartbeat(EngineHeartbeat heartbeat)
    {
        _lastTickUtc = heartbeat.TickUtc;
        _lastAppliedExe = heartbeat.LastAppliedExe;
        _lastAppliedUtc = heartbeat.LastAppliedUtc;
        Raise(nameof(StatusText));
    }

    /// Locks editing and rewrites the status line; the rows keep their last known state.
    public void ApplyFault(EngineFault fault)
    {
        if (_faulted) return;

        _faulted = true;
        SetPendingDelete(null);
        Raise(nameof(StatusText));
        Raise(nameof(IsEditable));
        Raise(nameof(LockedTooltip));
        Raise(nameof(SelectedInput));
    }

    public void AddRule(Rule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (!IsEditable) return;
        if (_rowsById.ContainsKey(rule.Id))
            throw new ArgumentException("a rule with this id already exists", nameof(rule));

        _rules = _rules.With(rule);
        AddRow(rule);
        _log.Information("rules", $"rule '{rule.ExeName}' created (all threads, disabled: no)");

        SelectedRuleId = rule.Id;
        _submit(_rules, new RuleChange(RuleChangeKind.Added, rule.Id));
        RequestSave();
        RaiseListCounts();
    }

    /// New mask and NeedsReview=false in one atomic step — the engine never sees the in-between.
    public void SetSelection(Guid ruleId, AffinityMask threads)
    {
        if (!IsEditable) return;
        if (_rules.ById(ruleId) is not { } rule) return;

        var updated = rule with { Threads = threads, NeedsReview = false };
        _rules = _rules.With(updated);
        if (_rowsById.TryGetValue(ruleId, out var row)) row.Apply(updated);

        _log.Information("rules",
            $"rule '{updated.ExeName}' selection changed ({threads.Count} threads: {_describeSelection(threads)})");
        _submit(_rules, new RuleChange(RuleChangeKind.SelectionChanged, ruleId));
        RequestSave();
        Raise(nameof(Banners));
        Raise(nameof(SelectedInput));
    }

    public void ToggleSelectedEnabled()
    {
        if (!IsEditable) return;
        if (_selectedRuleId is not { } id) return;
        if (_rules.ById(id) is not { } rule) return;

        var updated = rule with { Enabled = !rule.Enabled };
        _rules = _rules.With(updated);
        if (_rowsById.TryGetValue(id, out var row)) row.Apply(updated);

        _log.Information("rules", $"rule '{updated.ExeName}' {(updated.Enabled ? "enabled" : "disabled")}");
        _submit(_rules, new RuleChange(RuleChangeKind.EnabledChanged, id));
        RequestSave();
        Raise(nameof(SelectedInput));
    }

    /// The first call arms the inline confirmation, a second one on the same row deletes.
    public void DeleteSelectedRule()
    {
        if (!IsEditable) return;
        if (_selectedRuleId is not { } id) return;
        if (_rules.ById(id) is not { } rule) return;

        if (_pendingDelete != id)
        {
            SetPendingDelete(id);
            return;
        }

        SetPendingDelete(null);
        _rules = _rules.Without(id);
        if (_rowsById.Remove(id, out var row)) _rows.Remove(row);

        _log.Information("rules", $"rule '{rule.ExeName}' deleted");
        SelectedRuleId = null;
        _submit(_rules, new RuleChange(RuleChangeKind.Removed, id));
        RequestSave();
        RaiseListCounts();
    }

    public void CancelPendingDeleteConfirmation() => SetPendingDelete(null);

    /// Called on every move and resize, so a later flush only has to write.
    public void UpdateWindowBounds(WindowBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (bounds == _currentWindowBounds) return;

        _currentWindowBounds = bounds;
        Raise(nameof(CurrentWindowBounds));
        RequestSave();
    }

    private static string? LoadBanner(GuardReason reason, ConfigLoadOutcome outcome, string? detail) => reason switch
    {
        GuardReason.ConfigTooNew => BannerText.ConfigTooNew,
        GuardReason.ConfigUnreadable => BannerText.ConfigUnreadable,
        _ => outcome == ConfigLoadOutcome.Corrupt ? BannerText.Corrupt(detail) : null,
    };

    private void AddRow(Rule rule)
    {
        var row = new RuleRow(rule, InitialState(rule), _describeSelection);
        _rowsById[rule.Id] = row;

        int index = 0;
        while (index < _rows.Count
               && string.Compare(_rows[index].ExeName, rule.ExeName, StringComparison.OrdinalIgnoreCase) < 0)
            index++;

        _rows.Insert(index, row);
    }

    /// Until the engine reports, only what the rule itself already decides is shown.
    private RuleState InitialState(Rule rule) => RuleEvaluation.Check(rule, _machineMask) switch
    {
        RuleCheck.Disabled => RuleState.Disabled,
        RuleCheck.Invalid => RuleState.Invalid,
        _ => RuleState.Idle,
    };

    private void SetPendingDelete(Guid? ruleId)
    {
        if (_pendingDelete == ruleId) return;

        if (_pendingDelete is { } previous && _rowsById.TryGetValue(previous, out var old))
            old.SetConfirmingDelete(false);

        _pendingDelete = ruleId;

        if (ruleId is { } next && _rowsById.TryGetValue(next, out var row))
            row.SetConfirmingDelete(true);

        Raise(nameof(HasPendingDeleteConfirmation));
    }

    private void RequestSave() => _requestSave(BuildConfig());

    /// The measured machine only moves forward once no rule waits for a review.
    private AppConfig BuildConfig()
    {
        bool anyNeedsReview = _rules.Rules.Any(r => r.NeedsReview);
        var machine = anyNeedsReview ? _loadedMachine : _measuredMachine;

        return new AppConfig
        {
            SchemaVersion = _schemaVersion,
            Machine = machine,
            Settings = _settings with { WindowBounds = _currentWindowBounds },
            Rules = _rules,
        };
    }

    private void RaiseListCounts()
    {
        Raise(nameof(TotalRules));
        Raise(nameof(AppliedRules));
        Raise(nameof(HasRules));
        Raise(nameof(StatusText));
        Raise(nameof(Banners));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
