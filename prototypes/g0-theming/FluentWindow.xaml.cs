using System.Windows;

namespace G0Theming;

/// Weg A: ThemeMode is set declaratively in XAML (S03 D18). Only the runtime toggle needs
/// code-behind — see OnThemeApplied.
public partial class FluentWindow : DemoWindow
{
    public FluentWindow(RunOptions options, ThemeController theme)
        : base(options, theme)
    {
        InitializeComponent();
        AttachPanel(DemoContent);
    }

    protected override void OnThemeApplied(bool isLight)
    {
        // WPF0001: ThemeMode is still marked experimental in .NET 10 (S03 §3.2 point 2, D18).
        // The declarative XAML value covers the initial state; switching light/dark at runtime
        // has no declarative equivalent, and G0 has to compare both themes without touching the
        // user's system setting. Scope of the suppression is this single assignment.
#pragma warning disable WPF0001
        ThemeMode = isLight ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
    }
}
