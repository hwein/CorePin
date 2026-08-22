using System.Runtime.InteropServices;
using CorePin.Core.Diagnostics;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// The one notification icon: registration, tooltip, and the translation of its callback codes.
public sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    private readonly MessageWindow _window;
    private readonly ILog _log;
    private bool _added;
    private string _lastTooltip = "";      // UpdateIcon carries no tooltip and must not clear it

    public event Action? LeftClicked;
    public event Action? RightClicked;

    public TrayIcon(MessageWindow window, ILog log)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(log);
        _window = window;
        _log = log;
        _window.TrayCallback += OnTrayCallback;
    }

    internal enum TrayClick { None, Left, Right }

    /// NIN_KEYSELECT is the keyboard selection of the focused icon, a code of its own.
    internal static TrayClick Classify(uint eventCode) => eventCode switch
    {
        WM_CONTEXTMENU => TrayClick.Right,
        NIN_SELECT or NIN_KEYSELECT => TrayClick.Left,
        _ => TrayClick.None,
    };

    public void Show(nint hIcon, string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);

        _lastTooltip = tooltip;
        var data = BuildData(hIcon, tooltip);
        _added = Shell_NotifyIcon(NIM_ADD, in data);
        if (!_added)
        {
            _log.Warning("tray", "tray icon registration failed (NIM_ADD)");
            return;
        }

        data.UVersion = NOTIFYICON_VERSION_4;
        if (!Shell_NotifyIcon(NIM_SETVERSION, in data))
            _log.Warning("tray", "tray icon version negotiation failed (NIM_SETVERSION)");
    }

    public void UpdateIcon(nint hIcon)
    {
        var data = BuildData(hIcon, _lastTooltip);
        if (!Shell_NotifyIcon(NIM_MODIFY, in data))
            _log.Warning("tray", "tray icon update failed (NIM_MODIFY, icon)");
    }

    public void UpdateTooltip(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);

        _lastTooltip = tooltip;
        var data = BuildData(hIcon: 0, tooltip);
        data.UFlags = NIF_TIP | NIF_SHOWTIP;
        if (!Shell_NotifyIcon(NIM_MODIFY, in data))
            _log.Warning("tray", "tray icon update failed (NIM_MODIFY, tooltip)");
    }

    public void Remove()
    {
        if (!_added) return;

        var data = BuildData(hIcon: 0, tooltip: "");
        if (!Shell_NotifyIcon(NIM_DELETE, in data))
            _log.Warning("tray", "tray icon removal failed (NIM_DELETE)");
        _added = false;
    }

    public void Dispose() => Remove();

    private void OnTrayCallback(uint eventCode)
    {
        var click = Classify(eventCode);
        if (click == TrayClick.Left) LeftClicked?.Invoke();
        else if (click == TrayClick.Right) RightClicked?.Invoke();
    }

    private NOTIFYICONDATAW BuildData(nint hIcon, string tooltip)
    {
        var data = new NOTIFYICONDATAW
        {
            CbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            HWnd = _window.Handle,
            UID = IconId,
            // NIF_SHOWTIP: version 4 suppresses the standard tooltip without it.
            UFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP | NIF_SHOWTIP,
            UCallbackMessage = MessageWindow.TrayCallbackMessage,
            HIcon = hIcon,
        };
        data.SetTip(tooltip);
        return data;
    }
}
