using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CorePin.App.Themes;
using CorePin.Core.ViewModel;

namespace CorePin.App.Views;

public partial class MainWindow : Window
{
    private static readonly DoubleAnimation StateFade = CreateStateFade();

    private readonly Dictionary<RuleRow, (FrameworkElement StatusDot, FrameworkElement SecondLine)> _rowElements = [];

    public MainWindow(ThemeController theme)
    {
        InitializeComponent();
        theme.RegisterWindow(this);   // colour the title bar as soon as the HWND exists
    }

    private RuleListViewModel? ViewModel => DataContext as RuleListViewModel;

    private static DoubleAnimation CreateStateFade()
    {
        var fade = new DoubleAnimation(0.4, 1.0, new Duration(TimeSpan.FromMilliseconds(120)))
        {
            FillBehavior = FillBehavior.Stop,   // hand the row back to its own opacity afterwards
        };
        fade.Freeze();
        return fade;
    }

    /// A plain jump, no animation: the 120 ms fade stays reserved for state changes.
    public void ScrollRuleIntoView(Guid ruleId)
    {
        if (ViewModel?.RowById(ruleId) is not { } row) return;

        RuleList.ScrollIntoView(row);
    }

    /// Preview, not bubbling: ListBoxItem consumes Space for its own selection toggle first.
    /// Editing stays locked or unlocked in the view model; a blocked key is silent on purpose.
    private void RuleList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        switch (e.Key)
        {
            case Key.Space:
                viewModel.ToggleSelectedEnabled();
                e.Handled = true;
                break;
            case Key.Delete:
                viewModel.DeleteSelectedRule();
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;
        if (sender is not FrameworkElement { DataContext: RuleRow row }) return;

        viewModel.SelectedRuleId = row.RuleId;
        viewModel.DeleteSelectedRule();
    }

    private void RuleRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RuleRow row } element) return;
        if (element.FindName("StatusDot") is not FrameworkElement statusDot) return;
        if (element.FindName("SecondLineText") is not FrameworkElement secondLine) return;

        _rowElements[row] = (statusDot, secondLine);
        row.PropertyChanged -= OnRowChanged;   // a recycled container must not subscribe twice
        row.PropertyChanged += OnRowChanged;
    }

    private void RuleRow_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RuleRow row }) return;

        row.PropertyChanged -= OnRowChanged;
        _rowElements.Remove(row);
    }

    /// The one animation in the whole list: the status dot and second line fade on a state change.
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RuleRow.State)) return;
        if (sender is not RuleRow row || !_rowElements.TryGetValue(row, out var elements)) return;

        elements.StatusDot.BeginAnimation(OpacityProperty, StateFade);
        elements.SecondLine.BeginAnimation(OpacityProperty, StateFade);
    }
}
