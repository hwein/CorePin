using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
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
        window.SourceInitialized += (_, _) => DisableMaximize(window);
        // A restore can set Maximized without asking the style bit, so it snaps back here.
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState == WindowState.Maximized) window.WindowState = WindowState.Normal;
        };

        //     The engine only exists from step 6 on, so its submit goes through the field.
        _viewModel = new RuleListViewModel(
            _rules, _guard, _topology.MachineMask, _loaded.Config.SchemaVersion, _loaded.Config.Machine,
            new MachineInfo(_topology.CpuName, _topology.LogicalProcessorCount),
            _loaded.Config.Settings, _loaded.Outcome, _loaded.Detail, _loaded.RulesSkipped,
            ElevationInfo.IsElevated, (rules, change) => _engineHost.Submit(rules, change),
            _persister.RequestSave, mask => SelectionDescription.Describe(_topology, mask), _log);
        window.DataContext = _viewModel;

        //     A blocked guard makes the store discard every write, so it must not signal one.
        if (_guard.CanPersist) _persister.Saved += () => _viewModel.NotifySaved();

        //     The card view carries the rule id; the view model owns every rule change.
        window.CpuMap.Initialize(_topology, Theme, text => TopologyActions.TryCopyText(text, _log),
            () => TopologyActions.TryOpenIssuePage(_topology.CpuName, _log));
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

        // 5g. Rule creation: file dialog, picker flyout, and the icons of stored rules.
        RuleIconLoader.Initialize(_log);
        var inventory = new ProcessInventory();
        window.Flyout.Initialize(window.FromRunningButton, _viewModel,
            () => [.. inventory.ListOwnSession().Select(p => new ProcessRow(p.Pid, p.ExeName))],
            OnProcessPicked);
        window.FromRunningButton.Click += (_, _) => window.Flyout.OnTriggerClick();
        window.AddAppButton.Click += (_, _) => OnAddAppClick(window);
        foreach (var rule in _rules.Rules)
        {
            if (rule.LastKnownPath is not { } path) continue;

            var id = rule.Id;
            RuleIconLoader.LoadFromPath(path, icon =>
            {
                if (_viewModel.RowById(id) is { } row) row.Icon = icon;
            });
        }

        // 6. Engine and watcher.
        var engine = new AffinityEngine(inventory, new AffinityAccess(), _clock, _log,
                                        _topology.MachineMask);
        _engineHost = new EngineHost(engine, _clock, _log, _loaded.Config.Settings.PollIntervalMs);
        _engineBridge = new EngineBridge(_engineHost, Dispatcher, _viewModel);
        _engineHost.Submit(_rules, new RuleChange(RuleChangeKind.None, Guid.Empty));
        _engineHost.Start();

        // 7. Placed even with --tray: the window exists from the start and is never shown.
        //    The flyout hangs in its own HWND and cannot follow the window, so it closes.
        PlaceWindow(window);
        window.LocationChanged += (_, _) => { CaptureBounds(window); window.Flyout.CloseFlyout(); };
        window.SizeChanged += (_, _) => { CaptureBounds(window); window.Flyout.CloseFlyout(); };
        if (!_options.Tray) window.Show();
    }

    /// Without a stored position on an attached monitor the window opens over the tray.
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

            // The primary monitor's work area: its bottom right corner is where the tray sits.
            var area = SystemParameters.WorkArea;
            window.Left = area.Right - window.Width - Tokens.Spacing8;
            window.Top = area.Bottom - window.Height - Tokens.Spacing8;
        }
        catch (Exception ex)
        {
            // Best-effort placement: the window stays at its WPF default position instead of aborting startup.
            _log.Warning("app", $"window placement failed ({ex.GetType().Name}), using default position");
        }
    }

    private void DisableMaximize(Window window)
    {
        if (WindowStyling.DisableMaximize(new WindowInteropHelper(window).Handle)) return;

        _log.Warning("app", "WS_MAXIMIZEBOX could not be removed, the maximize button stays active");
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

        // A flyout that is open when editing locks up (Faulted) must not finish its pick.
        if (e.PropertyName == nameof(RuleListViewModel.IsEditable)
            && !_viewModel.IsEditable && MainWindow is Views.MainWindow lockedWindow)
            lockedWindow.Flyout.CloseFlyout();

        if (e.PropertyName is not (nameof(RuleListViewModel.TotalRules)
                                   or nameof(RuleListViewModel.AppliedRules))) return;

        _trayController.UpdateTooltip(_viewModel.TotalRules, _viewModel.AppliedRules);
    }

    /// A picked flyout row hands over what it already resolved; only a row still
    /// unresolved starts one late job, bound to the rule id, never to the flyout generation.
    private void OnProcessPicked(Views.FlyoutProcessRow row)
    {
        var result = _viewModel.AddOrSelect(row.ExeName, row.ResolvedPath);
        if (result.Rule is not { } rule) return;

        if (result.IsNew && row.ResolvedPath is null && !row.Resolved)
        {
            var id = rule.Id;
            RuleIconLoader.LoadFromProcess(row.Pid, (path, icon) =>
            {
                if (path is not null) _viewModel.PatchLastKnownPath(id, path);
                if (_viewModel.RowById(id) is { } lateRow) lateRow.Icon = icon;
            });
        }
        else
        {
            // A duplicate pick (result.IsNew false) stays fully inconsequential: selection/scroll only.
            if (result.IsNew && row.Icon is BitmapSource icon && _viewModel.RowById(rule.Id) is { } ruleRow)
                ruleRow.Icon = icon;
        }

        if (MainWindow is Views.MainWindow window) window.ScrollRuleIntoView(rule.Id);
    }

    private void OnAddAppClick(Views.MainWindow window)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add app",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(window) != true) return;

        string fileName = dialog.FileName;
        // The filter narrows the view, not the return value: a typed-in name can bypass it.
        if (!Path.GetExtension(fileName).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            _log.Debug("rules", "file dialog returned a non-exe path, ignored");
            return;
        }

        var result = _viewModel.AddOrSelect(Path.GetFileName(fileName), fileName);
        if (result.Rule is not { } rule) return;

        if (result.IsNew)
        {
            var id = rule.Id;
            RuleIconLoader.LoadFromPath(fileName, icon =>
            {
                if (_viewModel.RowById(id) is { } row) row.Icon = icon;
            });
        }
        window.ScrollRuleIntoView(rule.Id);
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
