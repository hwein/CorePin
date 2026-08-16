using System.Windows;

namespace G0Theming;

/// Shared Application behaviour. The two XAML shells differ only in whether they declare
/// Application.ThemeMode (S03 D18: declaratively in XAML, never assigned in code).
public class AppBase : Application
{
    protected AppBase(RunOptions options)
    {
        Options = options;
    }

    public RunOptions Options { get; }

    public ThemeController Theme { get; } = new();
}
