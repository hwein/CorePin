using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;

namespace G0Theming;

/// Trimmed copy of S03 §6.4: held wrapper dictionary instead of an index, per-window
/// registration for the dark title bar. WM_SETTINGCHANGE handling is out of scope for the
/// prototype (S03 §2.2) — the theme is switched application-internally only.
public sealed class ThemeController
{
    private readonly ResourceDictionary _themeSlot = new();
    private readonly List<Window> _registeredWindows = new();
    private bool _currentIsLight = true;

    public event Action<bool>? ThemeApplied;

    public bool IsLight => _currentIsLight;

    /// Last HRESULT of DwmSetWindowAttribute, for the report.
    public int LastTitleBarHResult { get; private set; }

    /// The system theme is read (S03 §6.1 code path, carried over) but only reported — the
    /// prototype starts in the theme the caller asks for, so a run is reproducible without
    /// touching any system setting.
    public bool SystemAppUsesLightTheme { get; private set; }

    public bool SystemUsesLightTheme { get; private set; }

    public void Initialize(Application app, bool startLight)
    {
        app.Resources.MergedDictionaries.Add(_themeSlot);
        SystemAppUsesLightTheme = ThemeSettings.ReadAppUsesLightTheme();
        SystemUsesLightTheme = ThemeSettings.ReadSystemUsesLightTheme();
        ApplyTheme(startLight);
    }

    public void RegisterWindow(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            _registeredWindows.Add(window);
            ApplyTitleBar(window, _currentIsLight);
        };
        window.Closed += (_, _) => _registeredWindows.Remove(window);
    }

    public void Toggle() => ApplyTheme(!_currentIsLight);

    /// S03 §6.2 contract point. In the prototype the caller is the watch-mode message hook, not
    /// S08's MessageWindow.
    public bool OnSystemThemeChanged()
    {
        SystemAppUsesLightTheme = ThemeSettings.ReadAppUsesLightTheme();
        SystemUsesLightTheme = ThemeSettings.ReadSystemUsesLightTheme();
        if (SystemAppUsesLightTheme == _currentIsLight) return false;
        ApplyTheme(SystemAppUsesLightTheme);
        return true;
    }

    public void SetLight(bool isLight)
    {
        if (isLight != _currentIsLight) ApplyTheme(isLight);
    }

    private void ApplyTheme(bool isLight)
    {
        _themeSlot.MergedDictionaries.Clear();
        _themeSlot.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(isLight ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative),
        });

        _currentIsLight = isLight;
        foreach (Window window in _registeredWindows) ApplyTitleBar(window, isLight);
        ThemeApplied?.Invoke(isLight);
    }

    private void ApplyTitleBar(Window window, bool isLight)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) LastTitleBarHResult = TitleBarTheme.SetDark(hwnd, dark: !isLight);
    }
}
