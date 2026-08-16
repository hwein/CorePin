using System;
using System.Windows;

namespace G0Theming;

/// Shared behaviour of the start variants. The two XAML shells differ only in whether they
/// declare Window.ThemeMode; Application.ThemeMode lives in FluentApp.xaml instead.
public abstract class DemoWindow : Window
{
    private DemoPanel? _panel;

    protected DemoWindow(RunOptions options, ThemeController theme)
    {
        Options = options;
        Theme = theme;
        theme.ThemeApplied += OnThemeApplied;
        Loaded += OnWindowLoaded;
    }

    public RunOptions Options { get; }

    public ThemeController Theme { get; }

    public DemoPanel Panel =>
        _panel ?? throw new InvalidOperationException("AttachPanel was not called.");

    protected void AttachPanel(DemoPanel panel)
    {
        _panel = panel;
        panel.ToggleThemeRequested += (_, _) => Theme.Toggle();
        Title = "CorePin — G0 theming prototype (" + Options.Name + ")";

        if (Options.FollowsSystemTheme)
        {
            panel.DisableThemeToggle(
                "Application.ThemeMode=System: the theme follows Windows. Change it in Windows "
                + "settings — the prototype reacts, it never assigns ThemeMode in code.");
        }
    }

    protected virtual void OnThemeApplied(bool isLight)
    {
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // --dark starts the controller dark before the window exists, so a declarative XAML
        // ThemeMode would still say Light. Only the window-level variants need this sync.
        if (Options.UsesWindowThemeMode && !Theme.IsLight) OnThemeApplied(false);

        if (Options.Watch) WatchMode.Attach(this);
        else if (Options.Capture) await CaptureRun.RunAsync(this);
    }
}
