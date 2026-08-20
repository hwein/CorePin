using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CorePin.Core.Rules;
using CorePin.Core.ViewModel;

namespace CorePin.App.Views;

/// The "+ From running…" picker: one process snapshot per open, live filter, lazy icons.
public partial class ProcessFlyout : UserControl
{
    public const string EmptyText = "No running programs found.";

    private RuleListViewModel? _viewModel;
    private Func<IReadOnlyList<ProcessRow>>? _snapshot;
    private Action<FlyoutProcessRow>? _commit;
    private ICollectionView? _view;
    private DateTime? _lastClosedAt;
    private int _generation;

    public ProcessFlyout() => InitializeComponent();

    public void Initialize(UIElement placementTarget, RuleListViewModel viewModel,
                           Func<IReadOnlyList<ProcessRow>> snapshot, Action<FlyoutProcessRow> commit)
    {
        FlyoutPopup.PlacementTarget = placementTarget;
        _viewModel = viewModel;
        _snapshot = snapshot;
        _commit = commit;
    }

    /// StaysOpen=false already closed the popup on this click's MouseDown, so a Closed
    /// timestamp younger than 150 ms means: this click was the closer, not an opener.
    public void OnTriggerClick()
    {
        if (FlyoutPopup.IsOpen) { FlyoutPopup.IsOpen = false; return; }
        if (_lastClosedAt is { } t && (DateTime.UtcNow - t) < TimeSpan.FromMilliseconds(150)) return;
        OpenFlyout();
    }

    /// A popup cannot follow its window and must not outlive a lost editing permission.
    public void CloseFlyout() => FlyoutPopup.IsOpen = false;

    private void OpenFlyout()
    {
        if (_snapshot is null) return;

        _generation++;
        int generation = _generation;

        var rows = ProcessRowBuilder.Build(_snapshot())
            .Select(p => new FlyoutProcessRow(p.Pid, p.ExeName)).ToList();

        _view = null;   // mutes TextChanged while the text resets
        SearchBox.Text = string.Empty;
        ProcessList.ItemsSource = rows;
        _view = CollectionViewSource.GetDefaultView(rows);
        _view.Filter = MatchesSearch;
        ProcessList.SelectedIndex = 0;

        bool empty = rows.Count == 0;
        ProcessList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        FlyoutPopup.IsOpen = true;

        // Queued in display order, so the rows visible before any scrolling load first.
        foreach (var row in rows)
        {
            var target = row;
            RuleIconLoader.LoadFromProcess(target.Pid, (path, icon) =>
            {
                if (generation != _generation) return;   // a reopened flyout has its own rows

                target.ResolvedPath = path;
                target.Icon = icon;
                target.Resolved = true;
            });
        }
    }

    private bool MatchesSearch(object candidate)
        => candidate is FlyoutProcessRow row
           && row.ExeName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase);

    /// The same path for Enter and double click; closing the flyout is the last step.
    private void Commit(FlyoutProcessRow row)
    {
        _commit?.Invoke(row);
        FlyoutPopup.IsOpen = false;
    }

    private void FlyoutPopup_Closed(object? sender, EventArgs e) => _lastClosedAt = DateTime.UtcNow;

    /// Loaded, not Popup.Opened: it fires once this element really is in the visual tree.
    private void SearchBox_Loaded(object sender, RoutedEventArgs e) => ((TextBox)sender).Focus();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_view is null) return;

        _view.Refresh();
        ProcessList.SelectedIndex = 0;
    }

    private void FlyoutRoot_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (ProcessList.SelectedItem is FlyoutProcessRow row) Commit(row);
                e.Handled = true;
                break;
            case Key.Escape:
                // A pending delete confirmation outranks the flyout; Handled keeps the window handler out.
                if (_viewModel is { HasPendingDeleteConfirmation: true } viewModel)
                    viewModel.CancelPendingDeleteConfirmation();
                else
                    FlyoutPopup.IsOpen = false;
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        int count = ProcessList.Items.Count;
        if (count == 0) return;

        ProcessList.SelectedIndex = Math.Clamp(ProcessList.SelectedIndex + delta, 0, count - 1);
        ProcessList.ScrollIntoView(ProcessList.SelectedItem);
    }

    /// One handler for single and double click: Handled=true below starves MouseDoubleClick,
    /// so ClickCount decides here, and the keyboard focus never leaves the search box.
    private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FlyoutProcessRow row) return;

        ProcessList.SelectedItem = row;
        if (e.ClickCount == 2) Commit(row);
        e.Handled = true;
        if (!SearchBox.IsKeyboardFocused) SearchBox.Focus();
    }
}
