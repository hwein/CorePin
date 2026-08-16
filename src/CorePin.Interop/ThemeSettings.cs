using Microsoft.Win32;

namespace CorePin.Interop;

/// AppsUseLightTheme drives the window, SystemUsesLightTheme the tray icon variant.
public static class ThemeSettings
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool ReadAppUsesLightTheme() => ReadPersonalizeFlag("AppsUseLightTheme");

    public static bool ReadSystemUsesLightTheme() => ReadPersonalizeFlag("SystemUsesLightTheme");

    private static bool ReadPersonalizeFlag(string valueName)
    {
        // Missing value means light — the state of a never personalised system.
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(valueName) is int v ? v != 0 : true;
    }
}
