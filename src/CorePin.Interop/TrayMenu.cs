using System.Runtime.InteropServices;
using CorePin.Core.Diagnostics;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

public enum TrayMenuItem
{
    None = 0, OpenCorePin = 1, CopyTopology = 2, OpenLogFolder = 3, Exit = 4, Autostart = 5,
}

/// The context menu of the tray icon: built on the click, shown, evaluated, destroyed.
public static class TrayMenu
{
    public static TrayMenuItem Show(MessageWindow window, bool autostartOn, ILog log)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(log);

        // WM_CONTEXTMENU carries no documented coordinates, so the cursor is the source.
        GetCursorPos(out POINT cursor);

        nint menu = CreatePopupMenu();
        if (menu == 0)
        {
            log.Warning("tray", $"tray menu could not be created, Win32 {Marshal.GetLastPInvokeError()}");
            return TrayMenuItem.None;
        }

        try
        {
            Append(menu, MF_STRING, (nuint)TrayMenuItem.OpenCorePin, "Open CorePin", log);
            Append(menu, MF_STRING, (nuint)TrayMenuItem.CopyTopology, "Copy topology", log);
            Append(menu, MF_STRING, (nuint)TrayMenuItem.OpenLogFolder, "Open log folder", log);
            Append(menu, autostartOn ? MF_STRING | MF_CHECKED : MF_STRING,
                   (nuint)TrayMenuItem.Autostart, "Start with Windows", log);
            Append(menu, MF_SEPARATOR, 0, null, log);
            Append(menu, MF_STRING, (nuint)TrayMenuItem.Exit, "Exit", log);

            // Without this the menu stays open when the user clicks somewhere else.
            if (!SetForegroundWindow(window.Handle))
                log.Warning("tray", "tray menu foreground request refused (SetForegroundWindow)");

            Marshal.SetLastPInvokeError(0);
            int command = TrackPopupMenu(
                menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN | TPM_RIGHTALIGN,
                cursor.X, cursor.Y, 0, window.Handle, 0);
            if (command == 0)
            {
                // Cancelling the menu also returns 0, but leaves the error at 0.
                int error = Marshal.GetLastPInvokeError();
                if (error != 0)
                    log.Warning("tray", $"tray menu could not be shown, Win32 {error}");
            }

            // Without this the menu closes by itself the second time it is opened.
            if (!PostMessage(window.Handle, WM_NULL, 0, 0))
                log.Warning("tray", $"tray menu dismiss message failed, Win32 {Marshal.GetLastPInvokeError()}");

            return FromCommand(command);
        }
        finally { DestroyMenu(menu); }
    }

    internal static TrayMenuItem FromCommand(int id) =>
        Enum.IsDefined((TrayMenuItem)id) ? (TrayMenuItem)id : TrayMenuItem.None;

    /// A menu entry that fails to append would vanish from the menu without a trace.
    private static void Append(nint menu, uint flags, nuint id, string? text, ILog log)
    {
        if (AppendMenu(menu, flags, id, text)) return;

        int error = Marshal.GetLastPInvokeError();
        log.Warning("tray", text is null
            ? $"tray menu separator could not be added, Win32 {error}"
            : $"tray menu item '{text}' could not be added, Win32 {error}");
    }
}
