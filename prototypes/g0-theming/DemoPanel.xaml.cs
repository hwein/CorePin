using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace G0Theming;

public partial class DemoPanel : UserControl
{
    private const int RuleCount = 16;   // > 15, S03 §2.2 — must overflow the 220 x 480 frame
    private const int CellCount = 48;   // S03 §2.2
    private const int SmtCellCount = 32;

    private static readonly string[] StatusKeys =
    {
        "CorePin.Brush.StatusApplied",
        "CorePin.Brush.StatusBlocked",
        "CorePin.Brush.StatusIdle",
    };

    private static readonly string[] SampleNames =
    {
        "cyberpunk2077.exe", "chrome.exe", "devenv.exe", "obs64.exe", "handbrake.exe",
        "blender.exe", "ffmpeg.exe", "notepad.exe", "code.exe", "steam.exe",
        "discord.exe", "msedge.exe", "photoshop.exe", "unrealeditor.exe", "dotnet.exe",
        "explorer.exe",
    };

    public DemoPanel()
    {
        InitializeComponent();
        Flyout.PlacementTarget = PopupButton;
        BuildRules();
        BuildCells();
        Loaded += OnPanelLoaded;
    }

    /// Raised by the "Toggle theme" button — the window owns the ThemeController.
    public event EventHandler? ToggleThemeRequested;

    /// The list item that gets keyboard focus, so the focus ring is visible in a screenshot.
    private ListBoxItem? _focusTarget;

    /// First SMT cell and its left half — measured, not assumed, for S03 §11 point 6.
    public FrameworkElement? FirstSmtShell { get; private set; }

    public Border? FirstSmtHalf { get; private set; }

    public Border? FirstSingleCell { get; private set; }

    /// First rule row — checks the 44 DIP row height at the running window (S03 §11 point 4).
    public ListBoxItem? FirstRuleRow { get; private set; }

    public bool FlyoutIsOpen => Flyout.IsOpen;

    /// The Border inside the Popup — the surface whose colour the pixel probe compares against
    /// CorePin.Color.CardBackground.
    public FrameworkElement FlyoutContent => FlyoutSurface;

    /// The ToolTip of the first rule row's second line, opened on demand for the probe.
    public ToolTip? SampleToolTip { get; private set; }

    public FrameworkElement? SampleToolTipTarget { get; private set; }

    public bool ToolTipIsOpen => SampleToolTip?.IsOpen == true;

    public void OpenToolTip()
    {
        if (SampleToolTip is null || SampleToolTipTarget is null) return;
        SampleToolTip.PlacementTarget = SampleToolTipTarget;
        SampleToolTip.Placement = PlacementMode.Bottom;
        SampleToolTip.StaysOpen = true;
        SampleToolTip.IsOpen = false;
        SampleToolTip.IsOpen = true;
    }

    public void CloseToolTip()
    {
        if (SampleToolTip is not null) SampleToolTip.IsOpen = false;
    }

    public void ShowWatchStatus(string text)
    {
        WatchStatus.Text = text;
        WatchStatus.Visibility = Visibility.Visible;
    }

    public void DisableThemeToggle(string reason)
    {
        ThemeButton.IsEnabled = false;
        ThemeButton.ToolTip = new ToolTip { Content = reason, HasDropShadow = false };
    }

    /// Forced close/open: re-setting IsOpen to true would be a no-op, which would hide the fact
    /// that something else closed the popup in between.
    public void OpenFlyout()
    {
        Flyout.IsOpen = false;
        Flyout.IsOpen = true;
    }

    public void CloseFlyout() => Flyout.IsOpen = false;

    /// Called again right before a capture: opening the flyout moves focus away, and the focus
    /// ring is one of the things G0 has to see (S03 §11 point 12). Programmatic Keyboard.Focus
    /// alone does not raise the focus visual — WPF only shows it while the keyboard is the most
    /// recent input device, so a real Down key has to go through the input manager.
    public void FocusRuleItem()
    {
        if (_focusTarget is null) return;
        Keyboard.Focus(_focusTarget);

        PresentationSource? source = PresentationSource.FromVisual(this);
        if (source is null) return;
        InputManager.Current.ProcessInput(
            new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Down)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            });
    }

    /// True if the card area actually produced a vertical scrollbar (checked, not assumed).
    public bool CardScrolls =>
        CardScroller.ComputedVerticalScrollBarVisibility == Visibility.Visible;

    public bool RuleListScrolls
    {
        get
        {
            ScrollViewer? viewer = FindScrollViewer(RuleList);
            return viewer is not null
                && viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible;
        }
    }

    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        RuleList.SelectedIndex = 3;
        if (_focusTarget is not null) Keyboard.Focus(_focusTarget);
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e) =>
        ToggleThemeRequested?.Invoke(this, EventArgs.Empty);

    private void OnTogglePopup(object sender, RoutedEventArgs e) => Flyout.IsOpen = !Flyout.IsOpen;

    private void BuildRules()
    {
        for (int i = 0; i < RuleCount; i++)
        {
            var item = new ListBoxItem
            {
                Height = 44,
                Padding = new Thickness(8, 0, 8, 0),
                Content = BuildRuleContent(i, showDeleteConfirmation: i == 3),
            };
            RuleList.Items.Add(item);
            FirstRuleRow ??= item;
            // One below the row that carries the inline delete confirmation: FocusRuleItem
            // presses Down from here, so that row ends up selected *and* keyboard focused.
            if (i == 2) _focusTarget = item;
        }
    }

    private Grid BuildRuleContent(int index, bool showDeleteConfirmation)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon placeholder, 20 px (01 §4).
        var icon = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(Border.BorderBrushProperty, "CorePin.Brush.Border");
        icon.SetResourceReference(Border.BackgroundProperty, "CorePin.Brush.CardBackground");
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        string secondLine =
            $"CPU 0–15 · mask 0x000000000000FFFF · applied {index + 1} min ago · {SampleNames[index]}";

        var firstLine = new TextBlock
        {
            Text = SampleNames[index],
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var tip = new ToolTip { Content = secondLine, HasDropShadow = false };
        var second = new TextBlock
        {
            Text = secondLine,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = tip,
        };
        second.SetResourceReference(TextBlock.ForegroundProperty, "CorePin.Brush.TextSecondary");

        if (SampleToolTip is null)
        {
            SampleToolTip = tip;
            SampleToolTipTarget = second;
        }

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(firstLine);
        lines.Children.Add(second);
        Grid.SetColumn(lines, 2);
        grid.Children.Add(lines);

        if (showDeleteConfirmation)
        {
            // 01 §4: inline delete confirmation inside the selected row — the classic clash
            // between selection foreground and button chrome.
            var confirm = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            confirm.Children.Add(new Button { Content = "Delete", Height = 22, MinWidth = 52, FontSize = 11 });
            confirm.Children.Add(new Button
            {
                Content = "Cancel",
                Height = 22,
                MinWidth = 52,
                FontSize = 11,
                Margin = new Thickness(4, 0, 0, 0),
            });
            Grid.SetColumn(confirm, 3);
            grid.Children.Add(confirm);
        }

        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        dot.SetResourceReference(Shape.FillProperty, StatusKeys[index % StatusKeys.Length]);
        Grid.SetColumn(dot, 4);
        grid.Children.Add(dot);

        return grid;
    }

    private void BuildCells()
    {
        for (int i = 0; i < CellCount; i++)
        {
            bool smt = i < SmtCellCount;
            if (smt)
            {
                // 01 §2 / S10 §3.2: 20 + 4 gap + 20. The 1 px outline sits on each half, never
                // on a shell around them — a shell border would shrink the halves below 20 x 28.
                var split = new Grid { Margin = new Thickness(0, 0, 4, 4) };
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

                Border left = BuildCellFace(filled: i < 6);
                Border right = BuildCellFace(filled: i < 3);
                Grid.SetColumn(left, 0);
                Grid.SetColumn(right, 2);
                split.Children.Add(left);
                split.Children.Add(right);

                if (FirstSmtHalf is null)
                {
                    FirstSmtShell = split;
                    FirstSmtHalf = left;
                }

                CellHost.Items.Add(split);
            }
            else
            {
                Border single = BuildCellFace(filled: false);
                single.Margin = new Thickness(0, 0, 4, 4);
                FirstSingleCell ??= single;
                CellHost.Items.Add(single);
            }
        }
    }

    private static Border BuildCellFace(bool filled)
    {
        var face = new Border
        {
            Width = 20,
            Height = 28,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
        };
        face.SetResourceReference(Border.BorderBrushProperty, "CorePin.Brush.Border");
        SetCellFill(face, filled);
        face.MouseLeftButtonDown += (s, e) =>
        {
            var b = (Border)s;
            SetCellFill(b, !(b.Tag is bool flag && flag));
            e.Handled = true;
        };
        return face;
    }

    private static void SetCellFill(Border cell, bool filled)
    {
        cell.Tag = filled;
        if (filled)
        {
            // S03 §5.3: system accent, never hard-coded, and DynamicResource so it stays live.
            cell.SetResourceReference(Border.BackgroundProperty, SystemColors.AccentColorBrushKey);
        }
        else
        {
            cell.SetResourceReference(Border.BackgroundProperty, "CorePin.Brush.CardBackground");
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer) return viewer;
            ScrollViewer? nested = FindScrollViewer(child);
            if (nested is not null) return nested;
        }

        return null;
    }
}
