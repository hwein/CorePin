using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CorePin.App.Themes;
using CorePin.Core.Configuration;
using CorePin.Core.Diagnostics;
using CorePin.Core.Engine;
using CorePin.Core.Rules;
using CorePin.Core.Time;
using CorePin.Core.Topology;
using CorePin.Interop;

namespace CorePin.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Application has no Dispose; OnExit releases the tray icon, controller and " +
                     "message window, and EngineHost was already stopped earlier in the exit sequence.")]
public partial class App : Application
{
    private readonly ILog _log;
    private readonly FileLog? _fileLog;      // ILog carries neither WriteAlways nor the directory in use
    private readonly IClock _clock;
    private readonly CpuTopology _topology;
    private readonly ConfigLoadResult _loaded;
    private readonly RuleSet _rules;
    private readonly StartupOptions _options;

    // Assigned in OnStartup before any caller can observe them.
    private MessageWindow _messageWindow = null!;
    private TrayIcon _trayIcon = null!;
    private TrayController _trayController = null!;
    private EngineHost _engineHost = null!;

    public App(ILog log, IClock clock, CpuTopology topology, ConfigStore config,
               ConfigLoadResult loaded, RuleSet rules, WriteGuard guard, StartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(guard);

        _log = log;
        _fileLog = log as FileLog;
        _clock = clock;
        _topology = topology;
        _loaded = loaded;
        _rules = rules;
        _options = options;
    }

    /// Plain instance, no static singleton.
    public ThemeController Theme { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // Not OnLastWindowClose: hiding into the tray must never end the process.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        // 5a/5b. Message window first — the tray icon needs its handle.
        _messageWindow = new MessageWindow(_log);
        _trayIcon = new TrayIcon(_messageWindow, _log);

        // 5c. Construct the window, do not show it.
        var window = new Views.MainWindow(Theme);
        MainWindow = window;

        // 5d. Subscribes first, shows the icon last.
        string logDirectory = _fileLog?.Directory ?? string.Empty;
        _trayController = new TrayController(_trayIcon, _messageWindow, window, _topology.Source,
                                             _rules.Rules.Count, logDirectory, _log);

        // 5e. The window theme is a separate registry value from the tray icon variant.
        _messageWindow.SettingChanged += Theme.OnSystemThemeChanged;

        // 5f. Wired from here, not inside the window: it must not know TrayController.
        window.Closing += MainWindow_Closing;
        window.KeyDown += MainWindow_KeyDown;

        _trayController.ExitRequested += OnExitRequested;

        // 6. Engine and watcher.
        var engine = new AffinityEngine(new ProcessInventory(), new AffinityAccess(), _clock, _log,
                                        _topology.MachineMask);
        _engineHost = new EngineHost(engine, _clock, _log, _loaded.Config.Settings.PollIntervalMs);
        _engineHost.Submit(_rules, new RuleChange(RuleChangeKind.None, Guid.Empty));
        _engineHost.Start();

        // 7. With --tray the window exists from the start, but is never shown.
        if (!_options.Tray) window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon.Dispose();          // NIM_DELETE, a no-op after the exit sequence already removed it
        _trayController.Dispose();
        // EngineHost.Stop already ran in the exit sequence; Dispose would repeat the join wait.
        _messageWindow.Dispose();
        base.OnExit(e);
    }

    private void OnExitRequested()
    {
        _engineHost.Stop();
        _fileLog?.WriteAlways(LogLevel.Information, "app", "CorePin exiting (code 0)");
        _trayIcon.Remove();
        Shutdown();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_trayController.IsExiting) return;

        e.Cancel = true;
        _trayController.HideToTray();
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        e.Handled = true;
        _trayController.HideToTray();
    }
}
