using System.Runtime.InteropServices;
using CorePin.Core.Diagnostics;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// The one hidden window of the process: broadcast receiver, WM_COPYDATA target, tray callback.
public sealed class MessageWindow : IDisposable
{
    internal const string ClassName = "CorePin_MsgWnd";
    internal const uint TrayCallbackMessage = 0x8000 + 1;      // WM_APP + 1

    private readonly WndProcDelegate _wndProc;                 // field, not a local: the GC must not collect it
    private readonly uint _taskbarCreatedMessage;
    private readonly ILog _log;
    private bool _destroyed;

    public nint Handle { get; }

    public event Action? TaskbarCreated;
    public event Action? SettingChanged;
    public event Action<string>? CopyDataReceived;
    public event Action? DpiChanged;
    internal event Action<uint>? TrayCallback;

    public MessageWindow(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _wndProc = WndProc;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        nint instance = GetModuleHandle(null);
        RegisterClass(instance);

        // Message-only windows never receive broadcasts, so this stays a plain top-level window.
        Handle = CreateWindowEx(0, ClassName, "", WS_OVERLAPPED, 0, 0, 0, 0, 0, 0, instance, 0);
        if (Handle == 0)
        {
            throw new InvalidOperationException(
                $"CreateWindowEx for {ClassName} failed: {Marshal.GetLastPInvokeError()}");
        }

        AllowMessageFromLowerIntegrity(WM_COPYDATA);
        AllowMessageFromLowerIntegrity(_taskbarCreatedMessage);
        AllowMessageFromLowerIntegrity(WM_SETTINGCHANGE);
        AllowMessageFromLowerIntegrity(TrayCallbackMessage);
    }

    public void AllowMessageFromLowerIntegrity(uint message)
    {
        if (ChangeWindowMessageFilterEx(Handle, message, MSGFLT_ALLOW, 0)) return;

        int error = Marshal.GetLastPInvokeError();
        _log.Warning("interop", $"ChangeWindowMessageFilterEx failed for message 0x{message:X4}, Win32 {error}");
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        DestroyWindow(Handle);            // no UnregisterClass: the process ends right after
    }

    private void RegisterClass(nint instance)
    {
        nint className = Marshal.StringToHGlobalUni(ClassName);
        try
        {
            var window = new WNDCLASSEXW
            {
                CbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                HInstance = instance,
                LpszClassName = className,
            };
            RegisterClassEx(in window);   // once per process: MessageWindow is a singleton
        }
        finally { Marshal.FreeHGlobal(className); }   // the registered class keeps its own copy
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_COPYDATA)
        {
            CopyDataReceived?.Invoke(ReadCopyData(lParam));
            return 1;                                   // TRUE: the message was processed
        }
        // A failed RegisterWindowMessage would leave 0 here, and 0 is WM_NULL, which we post.
        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            TaskbarCreated?.Invoke();
            return 0;
        }
        if (msg == WM_SETTINGCHANGE)
        {
            string? setting = lParam == 0 ? null : Marshal.PtrToStringUni(lParam);
            if (setting == "ImmersiveColorSet") SettingChanged?.Invoke();
            return 0;
        }
        if (msg == WM_DPICHANGED)
        {
            DpiChanged?.Invoke();          // payload-free: subscribers ask for the size they need
            return 0;
        }
        if (msg == TrayCallbackMessage)
        {
            TrayCallback?.Invoke(ExtractTrayEvent(lParam));
            return 0;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// LOWORD(lParam) — where version 4 of the notification icon puts the event code.
    internal static uint ExtractTrayEvent(nint lParam) => (uint)(lParam.ToInt64() & 0xFFFF);

    private static string ReadCopyData(nint lParam)
    {
        if (lParam == 0) return "";

        var data = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
        return data.CbData > 0 && data.LpData != 0
            ? Marshal.PtrToStringUni(data.LpData, data.CbData / sizeof(char))
            : "";
    }
}
