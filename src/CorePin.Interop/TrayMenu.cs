using CorePin.Core.Autostart;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

public enum TrayMenuItem
{
    None = 0, OpenCorePin = 1, CopyTopology = 2, OpenLogFolder = 3, Exit = 4,
    AutostartOff = 5, AutostartNormal = 6, AutostartAdmin = 7,
}

/// The context menu of the tray icon: built on the click, shown, evaluated, destroyed.
public static class TrayMenu
{
    public static TrayMenuItem Show(MessageWindow window, TrayMenuState state)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(state);

        // WM_CONTEXTMENU carries no documented coordinates, so the cursor is the source.
        GetCursorPos(out POINT cursor);

        nint menu = CreatePopupMenu();
        if (menu == 0) return TrayMenuItem.None;

        try
        {
            AppendMenu(menu, MF_STRING, (nuint)TrayMenuItem.OpenCorePin, "Open CorePin");
            AppendMenu(menu, MF_STRING, (nuint)TrayMenuItem.CopyTopology, "Copy topology");
            AppendMenu(menu, MF_STRING, (nuint)TrayMenuItem.OpenLogFolder, "Open log folder");
            AppendAutostart(menu, state);
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

    private static void AppendAutostart(nint menu, TrayMenuState state)
    {
        nint subMenu = CreatePopupMenu();
        if (subMenu == 0) return;

        AppendMenu(subMenu, Flags(state.Mode == AutostartMode.Off),
                   (nuint)TrayMenuItem.AutostartOff, "Off");
        AppendMenu(subMenu, Flags(state.Mode == AutostartMode.Normal),
                   (nuint)TrayMenuItem.AutostartNormal, NormalText(state));
        uint adminFlags = Flags(state.Mode == AutostartMode.Admin)
                          | (state.AdminSelectable ? 0u : MF_GRAYED);
        AppendMenu(subMenu, adminFlags, (nuint)TrayMenuItem.AutostartAdmin, AdminText(state));

        // AppendMenu hands the submenu over: DestroyMenu on the owner frees it as well.
        if (!AppendMenu(menu, MF_POPUP | MF_STRING, (nuint)subMenu, "Start with Windows"))
            DestroyMenu(subMenu);
    }

    private static uint Flags(bool isCurrentMode) => isCurrentMode ? MF_STRING | MF_CHECKED : MF_STRING;

    private static string NormalText(TrayMenuState state)
        => state.NormalDisabledInTaskManager ? "Normal (turned off in Task Manager)" : "Normal";

    private static string AdminText(TrayMenuState state)
    {
        if (!state.AdminSelectable) return "As administrator (needs an administrator account)";

        return state.AdminStale
            ? "As administrator (location changed – select to repair)"
            : "As administrator";
    }
}
