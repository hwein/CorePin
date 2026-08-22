using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

public enum TrayMenuItem
{
    None = 0, OpenCorePin = 1, CopyTopology = 2, OpenLogFolder = 3, Exit = 4, Autostart = 5,
}

/// The context menu of the tray icon: built on the click, shown, evaluated, destroyed.
public static class TrayMenu
{
    public static TrayMenuItem Show(MessageWindow window, bool autostartOn)
    {
        ArgumentNullException.ThrowIfNull(window);

        // WM_CONTEXTMENU carries no documented coordinates, so the cursor is the source.
        GetCursorPos(out POINT cursor);

        nint menu = CreatePopupMenu();
        if (menu == 0) return TrayMenuItem.None;

        try
        {
            AppendMenu(menu, MF_STRING, (nuint)TrayMenuItem.OpenCorePin, "Open CorePin");
            AppendMenu(menu, MF_STRING, (nuint)TrayMenuItem.CopyTopology, "Copy topology");
            AppendMenu(menu, MF_STRING, (nuint)TrayMenuItem.OpenLogFolder, "Open log folder");
            AppendMenu(menu, autostartOn ? MF_STRING | MF_CHECKED : MF_STRING,
                       (nuint)TrayMenuItem.Autostart, "Start with Windows");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, (nuint)TrayMenuItem.Exit, "Exit");

            // Without this the menu stays open when the user clicks somewhere else.
            SetForegroundWindow(window.Handle);
            int command = TrackPopupMenu(
                menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN | TPM_RIGHTALIGN,
                cursor.X, cursor.Y, 0, window.Handle, 0);
            // Without this the menu closes by itself the second time it is opened.
            PostMessage(window.Handle, WM_NULL, 0, 0);

            return FromCommand(command);
        }
        finally { DestroyMenu(menu); }
    }

    internal static TrayMenuItem FromCommand(int id) =>
        Enum.IsDefined((TrayMenuItem)id) ? (TrayMenuItem)id : TrayMenuItem.None;
}
