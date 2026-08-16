using System.Windows;
using CorePin.App.Themes;

namespace CorePin.App;

/// No constructor parameters yet: nothing here reads a dependency.
public partial class App : Application
{
    /// Plain instance, no static singleton.
    public ThemeController Theme { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new Views.MainWindow(Theme);
        MainWindow = window;
        window.Show();
    }
}
