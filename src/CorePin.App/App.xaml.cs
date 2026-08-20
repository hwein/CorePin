using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CorePin.App.Themes;
using CorePin.Core.Configuration;
using CorePin.Core.Diagnostics;
using CorePin.Core.Engine;
using CorePin.Core.Layout;
using CorePin.Core.Rules;
using CorePin.Core.Time;
using CorePin.Core.Topology;
using CorePin.Core.ViewModel;
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
    private readonly ConfigStore _config;
    private readonly ConfigLoadResult _loaded;
    private readonly RuleSet _rules;
    private readonly WriteGuard _guard;
    private readonly StartupOptions _options;

    // Assigned in OnStartup before any caller can observe them.
    private MessageWindow _messageWindow = null!;
    private TrayIcon _trayIcon = null!;
    private TrayController _trayController = null!;
    private EngineHost _engineHost = null!;
    private ConfigPersister _persister = null!;
    private RuleListViewModel _viewModel = null!;
    private EngineBridge _engineBridge = null!;   // held only so its subscriptions stay alive

    public App(ILog log, IClock clock, CpuTopology topology, ConfigStore config,
               ConfigLoadResult loaded, RuleSet rules, WriteGuard guard, StartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(guard);

        _log = log;
        _fileLog = log as FileLog;
        _clock = clock;
        _topology = topology;
        _config = config;
        _loaded = loaded;
        _rules = rules;
        _guard = guard;
        _options = options;
    }

    /// Plain instance, no static singleton.
    public ThemeController Theme { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // Not OnLastWindowClose: hiding into the tray must never end the process.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        // Before the tray: HideToTray flushes through it.
        _persister = new ConfigPersister(_config.Save, Dispatcher);

        // 5a/5b. Message window first — the tray icon needs its handle.
        _messageWindow = new MessageWindow(_log);
        _trayIcon = new TrayIcon(_messageWindow, _log);

        // 5c. Construct the window, do not show it.
        var window = new Views.MainWindow(Theme);
        MainWindow = window;

        //     The engine only exists from step 6 on, so its submit goes through the field.
        _viewModel = new RuleListViewModel(
            _rules, _guard, _topology.MachineMask, _loaded.Config.SchemaVersion, _loaded.Config.Machine,
            new MachineInfo(_topology.CpuName, _topology.LogicalProcessorCount),
            _loaded.Config.Settings, _loaded.Outcome, _loaded.Detail, _loaded.RulesSkipped,
            ElevationInfo.IsElevated, (rules, change) => _engineHost.Submit(rules, change),
            _persister.RequestSave, mask => SelectionDescription.Describe(_topology, mask), _log);
        window.DataContext = _viewModel;

        //     The card view carries the rule id; the view model owns every rule change.
        window.CpuMap.Initialize(_topology, Theme, text => TopologyActions.TryCopyText(text, _log));
        window.CpuMap.SelectionChanged += (ruleId, mask) => _viewModel.SetSelection(ruleId, mask);
        window.CpuMap.RuleEnabledChanged += (id, _) =>
        {
            if (id == _viewModel.SelectedRuleId) _viewModel.ToggleSelectedEnabled();
        };
        window.CpuMap.SetRule(_viewModel.SelectedRuleId, _viewModel.SelectedInput, _viewModel.LockedTooltip);

        // 5d. Subscribes first, shows the icon last.
        string logDirectory = _fileLog?.Directory ?? string.Empty;
        _trayController = new TrayController(_trayIcon, _messageWindow, window, _topology.Source,
                                             _rules.Rules.Count, logDirectory, _persister, _log);

        // 5e. The window theme is a separate registry value from the tray icon variant.
        _messageWindow.SettingChanged += Theme.OnSystemThemeChanged;

        // 5f. Wired from here, not inside the window: it must not know TrayController.
        window.Closing += MainWindow_Closing;
        window.KeyDown += MainWindow_KeyDown;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _trayController.UpdateTooltip(_viewModel.TotalRules, _viewModel.AppliedRules);
        _trayController.ExitRequested += OnExitRequested;

        // 6. Engine and watcher.
        var engine = new AffinityEngine(new ProcessInventory(), new AffinityAccess(), _clock, _log,
                                        _topology.MachineMask);
        _engineHost = new EngineHost(engine, _clock, _log, _loaded.Config.Settings.PollIntervalMs);
        _engineBridge = new EngineBridge(_engineHost, Dispatcher, _viewModel);
        _engineHost.Submit(_rules, new RuleChange(RuleChangeKind.None, Guid.Empty));
        _engineHost.Start();

        // 7. Placed even with --tray: the window exists from the start and is never shown.
        PlaceWindow(window);
        window.LocationChanged += (_, _) => CaptureBounds(window);
        window.SizeChanged += (_, _) => CaptureBounds(window);
        if (!_options.Tray) window.Show();
    }

    /// A stored position on no attached monitor is dropped: it would open in nowhere.
    private void PlaceWindow(Window window)
    {
        try
        {
            if (_viewModel.CurrentWindowBounds is { } bounds
                && MonitorHelper.IntersectsAnyMonitor(new DipRect(bounds.X, bounds.Y, bounds.W, bounds.H)))
            {
                window.Left = bounds.X;
                window.Top = bounds.Y;
                window.Width = bounds.W;
                window.Height = bounds.H;
                return;
            }

            var area = MonitorHelper.WorkAreaAtCursor();
            window.Left = area.X + ((area.Width - window.Width) / 2);
            window.Top = area.Y + ((area.Height - window.Height) / 2);
        }
        catch (Exception ex)
        {
            // Best-effort placement: the window stays at its WPF default position instead of aborting startup.
            _log.Warning("app", $"window placement failed ({ex.GetType().Name}), using default position");
        }
    }

    /// Kept up to date on every move and resize, so hiding and exiting only have to write.
    private void CaptureBounds(Window window)
    {
        var rect = window.WindowState != WindowState.Normal
            ? window.RestoreBounds
            : new Rect(window.Left, window.Top, window.Width, window.Height);
        if (double.IsNaN(rect.X) || double.IsNaN(rect.Y) || rect.IsEmpty) return;

        _viewModel.UpdateWindowBounds(new WindowBounds(
            (int)Math.Round(rect.X), (int)Math.Round(rect.Y),
            (int)Math.Round(rect.Width), (int)Math.Round(rect.Height)));
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SelectedInput covers rule switches, selection edits, enable toggles and Faulted.
        if (e.PropertyName == nameof(RuleListViewModel.SelectedInput) && MainWindow is Views.MainWindow window)
            window.CpuMap.SetRule(_viewModel.SelectedRuleId, _viewModel.SelectedInput, _viewModel.LockedTooltip);

        if (e.PropertyName is not (nameof(RuleListViewModel.TotalRules)
                                   or nameof(RuleListViewModel.AppliedRules))) return;

        _trayController.UpdateTooltip(_viewModel.TotalRules, _viewModel.AppliedRules);
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
        _persister.FlushNow();
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
        if (((Window)sender).DataContext is not RuleListViewModel viewModel) return;

        if (viewModel.HasPendingDeleteConfirmation)
        {
            viewModel.CancelPendingDeleteConfirmation();
            e.Handled = true;
            return;
        }

        e.Handled = true;
        _trayController.HideToTray();
    }
}
