using System.Windows;
using CorePin.App.Themes;

namespace CorePin.App;

/// No constructor parameters in phase 0: nothing here reads a dependency yet, and the log
/// lives in Program.Main. The eight-parameter signature from S01 §3.9 arrives with the
/// types from S05/S06/S08.
public partial class App : Application
{
    /// Plain instance, no static singleton (S03 §6.4).
    public ThemeController Theme { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new Views.MainWindow(Theme);
        MainWindow = window;
        window.Show();
    }
}
