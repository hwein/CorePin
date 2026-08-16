using System;
using Microsoft.Win32;

namespace G0Theming;

/// Copy of the S03 §6.1 registry read for the throw-away prototype.
internal static class ThemeSettings
{
    public static bool ReadAppUsesLightTheme() => ReadPersonalizeFlag("AppsUseLightTheme");

    public static bool ReadSystemUsesLightTheme() => ReadPersonalizeFlag("SystemUsesLightTheme");

    private static bool ReadPersonalizeFlag(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue(valueName) is int v ? v != 0 : true;
    }
}

/// Copy of the S03 §6.3 wrapper around DwmSetWindowAttribute.
internal static class TitleBarTheme
{
    public static int SetDark(IntPtr hwnd, bool dark)
    {
        int value = dark ? 1 : 0;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, in value, sizeof(int));
    }
}
