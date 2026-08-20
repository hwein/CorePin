using System.ComponentModel;
using CorePin.Core.Engine;
using CorePin.Core.Primitives;
using CorePin.Core.Rules;

namespace CorePin.Core.ViewModel;

/// One line of the rule list. Every member expects the UI thread.
public sealed class RuleRow : INotifyPropertyChanged
{
    private readonly Func<AffinityMask, string> _describe;
    private Rule _rule;
    private RuleStatus _status;
    private bool _confirmingDelete;
    private object? _icon;

    internal RuleRow(Rule rule, RuleState initialState, Func<AffinityMask, string> describe)
    {
        _rule = rule;
        _describe = describe;
        _status = new RuleStatus(rule.Id, initialState, 0, 0, 0, BlockReason.None);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid RuleId => _rule.Id;

    public string ExeName => _rule.ExeName;

    /// 24 = the budget of line 1, narrower than the second line's since it shares space with dot and icon.
    public string DisplayName => LineBudget.Fit(ExeName, 24);

    public RuleState State => _status.State;

    public char Symbol => RuleRowText.Symbol(State);

    /// Only the reported state dims a row, never Rule.Enabled.
    public bool IsDimmed => State == RuleState.Disabled;

    public bool IsConfirmingDelete => _confirmingDelete;

    /// A BitmapSource at runtime; typed as object so this assembly stays WPF-free.
    public object? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;

            _icon = value;
            Raise(nameof(Icon));
        }
    }

    public string SecondLine => LineBudget.Fit(
        _confirmingDelete ? RuleRowText.DeleteConfirmation : RuleRowText.SecondLine(_status));

    public string Tooltip
    {
        get
        {
            var parts = new List<string>(5) { _rule.ExeName, RuleRowText.SecondLine(_status) };
            if (RuleRowText.Explanation(_status) is { } explanation) parts.Add(explanation);
            parts.Add($"{_rule.Threads.Count} thr · {_describe(_rule.Threads)}");
            if (_rule.LastKnownPath is { } path) parts.Add(path);
            return string.Join(Environment.NewLine, parts);
        }
    }

    /// Moves dot and state, never an open confirmation text; false when nothing changed.
    internal bool Apply(RuleStatus status)
    {
        if (status == _status) return false;

        _status = status;
        Raise(nameof(State));
        Raise(nameof(Symbol));
        Raise(nameof(IsDimmed));
        Raise(nameof(Tooltip));
        if (!_confirmingDelete) Raise(nameof(SecondLine));
        return true;
    }

    internal void Apply(Rule rule)
    {
        _rule = rule;
        Raise(nameof(Tooltip));
        Raise(nameof(DisplayName));
    }

    internal void SetConfirmingDelete(bool value)
    {
        if (_confirmingDelete == value) return;

        _confirmingDelete = value;
        Raise(nameof(IsConfirmingDelete));
        Raise(nameof(SecondLine));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
