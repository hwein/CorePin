using System.Windows;
using CorePin.Core.Diagnostics;
using CorePin.Core.Topology;
using CorePin.Interop;

namespace CorePin.App;

/// Owns the icon variant, its size and the tooltip; TrayIcon knows neither theme nor DPI.
internal sealed class TrayController : IDisposable
{
    private const string LightIconResource = "CorePin.App.Assets.tray-light.ico";
    private const string DarkIconResource = "CorePin.App.Assets.tray-dark.ico";

    private readonly TrayIcon _trayIcon;
    private readonly MessageWindow _messageWindow;
    private readonly Window _mainWindow;
    private readonly TopologySnapshot _topologySource;
    private readonly string _logDirectory;
    private readonly ConfigPersister _configPersister;
    private readonly AutostartController _autostart;
    private readonly ILog _log;

    private byte[]? _lightIconBytes;
    private byte[]? _darkIconBytes;
    private nint _currentIcon;
    private bool _useLightIcon;
    private int _currentSizePx;
    private string _lastTooltip;

    public TrayController(TrayIcon trayIcon, MessageWindow messageWindow, Window mainWindow,
                          TopologySnapshot topologySource, int ruleCount, string logDirectory,
                          ConfigPersister configPersister, AutostartController autostart, ILog log)
    {
        _trayIcon = trayIcon;
        _messageWindow = messageWindow;
        _mainWindow = mainWindow;
        _topologySource = topologySource;
        _logDirectory = logDirectory;
        _configPersister = configPersister;
        _autostart = autostart;
        _log = log;

        trayIcon.LeftClicked += OnLeftClicked;
        trayIcon.RightClicked += OnRightClicked;
        messageWindow.TaskbarCreated += OnTaskbarCreated;
        messageWindow.CopyDataReceived += OnCopyDataReceived;
        messageWindow.DpiChanged += OnDpiChanged;
        messageWindow.SettingChanged += OnSettingChanged;

        // Only now: the icon must never exist before its re-registration is wired up.
        _useLightIcon = !ThemeSettings.ReadSystemUsesLightTheme();
        _currentSizePx = IconResources.TrayIconSizeFor(messageWindow.Handle);
        _currentIcon = LoadIcon(_useLightIcon, _currentSizePx);
        if (_currentIcon == 0)
            _log.Warning("tray", $"tray icon load failed ({IconVariantName(_useLightIcon)} at {_currentSizePx} px)");
        _lastTooltip = TrayTooltip.Format(ruleCount, 0);
        // Menu and clicks stay reachable even with a blank icon; a load failure must not abort startup.
        _trayIcon.Show(_currentIcon, _lastTooltip);
    }

    public event Action? ExitRequested;

    public bool IsExiting { get; private set; }

    public void UpdateTooltip(int totalRules, int appliedRules)
    {
        string tooltip = TrayTooltip.Format(totalRules, appliedRules);
        if (tooltip == _lastTooltip) return;

        _lastTooltip = tooltip;
        _trayIcon.UpdateTooltip(tooltip);
    }

    public void ToggleMainWindow()
    {
        if (!_mainWindow.IsVisible || _mainWindow.WindowState == WindowState.Minimized)
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Show();
            _mainWindow.Activate();
        }
        else if (_mainWindow.IsActive)
        {
            HideToTray();
        }
        else
        {
            _mainWindow.Activate();
        }
    }

    /// The only place that hides the window; the close button routes here as well.
    public void HideToTray()
    {
        _configPersister.FlushNow();   // the geometry is already current, this only writes it
        _mainWindow.Hide();
    }

    public void Dispose()
    {
        if (_currentIcon == 0) return;

        IconResources.Destroy(_currentIcon);
        _currentIcon = 0;
    }

    private void OnLeftClicked() => ToggleMainWindow();

    private void OnRightClicked()
    {
        // Read before the menu, act on the same reading: this is what the user just saw.
        var autostart = _autostart.Read();
        switch (TrayMenu.Show(_messageWindow, autostart.On))
        {
            case TrayMenuItem.OpenCorePin:
                ToggleMainWindow();
                break;
            case TrayMenuItem.CopyTopology:
                TopologyActions.CopyTopology(_topologySource, _log);
                break;
            case TrayMenuItem.OpenLogFolder:
                LogFolder.TryOpen(_logDirectory, _log);
                break;
            case TrayMenuItem.Autostart:
                _autostart.Toggle(autostart);
                break;
            case TrayMenuItem.Exit:
                OnExitClicked();
                break;
            default:
                break;
        }
    }

    private void OnCopyDataReceived(string _)
    {
        _log.Information("interop", "second instance detected, bringing existing window forward");
        ToggleMainWindow();
    }

    /// A restarted Explorer forgot the icon: NIM_ADD again, with the values kept here.
    private void OnTaskbarCreated() => _trayIcon.Show(_currentIcon, _lastTooltip);

    private void OnSettingChanged()
    {
        bool useLight = !ThemeSettings.ReadSystemUsesLightTheme();
        if (useLight == _useLightIcon) return;

        nint icon = LoadIcon(useLight, _currentSizePx);
        if (icon == 0)
        {
            // Keep the icon currently shown; leave _useLightIcon so a later change retries the load.
            _log.Warning("tray", $"tray icon load failed ({IconVariantName(useLight)} at {_currentSizePx} px)");
            return;
        }

        _useLightIcon = useLight;
        ReplaceIcon(icon);
    }

    private void OnDpiChanged()
    {
        int sizePx = IconResources.TrayIconSizeFor(_messageWindow.Handle);
        if (sizePx == _currentSizePx) return;

        nint icon = LoadIcon(_useLightIcon, sizePx);
        if (icon == 0)
        {
            // Keep the icon currently shown; leave _currentSizePx so a later change retries the load.
            _log.Warning("tray", $"tray icon load failed ({IconVariantName(_useLightIcon)} at {sizePx} px)");
            return;
        }

        _currentSizePx = sizePx;
        ReplaceIcon(icon);
    }

    private void OnExitClicked()
    {
        if (IsExiting) return;

        IsExiting = true;
        ExitRequested?.Invoke();
    }

    /// Shell_NotifyIcon takes no ownership, so the previous handle is ours to free.
    private void ReplaceIcon(nint icon)
    {
        nint previous = _currentIcon;
        _currentIcon = icon;
        _trayIcon.UpdateIcon(icon);
        if (previous != 0) IconResources.Destroy(previous);
    }

    private nint LoadIcon(bool useLight, int sizePx)
        => IconResources.LoadNearestFrame(IconBytes(useLight), sizePx);

    private static string IconVariantName(bool useLight) => useLight ? "tray-light" : "tray-dark";

    private byte[] IconBytes(bool useLight)
        => useLight
            ? _lightIconBytes ??= ReadResource(LightIconResource)
            : _darkIconBytes ??= ReadResource(DarkIconResource);

    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(TrayController).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"embedded resource {name} is missing");
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
