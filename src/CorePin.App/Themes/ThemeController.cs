using System.Windows;
using System.Windows.Interop;
using CorePin.Interop;

namespace CorePin.App.Themes;

public sealed class ThemeController
{
    private readonly ResourceDictionary _themeSlot = new();   // held instance, never an index
    private readonly List<Window> _registeredWindows = new();
    private bool _currentIsLight;

    /// Subscribed by self-drawing controls (S10) to call InvalidateVisual on themselves —
    /// ThemeController does not know CpuMapControl and never calls it directly.
    public event Action? ThemeApplied;

    public void Initialize(Application app)
    {
        app.Resources.MergedDictionaries.Add(_themeSlot);     // once, after Tokens.xaml
        _currentIsLight = ThemeSettings.ReadAppUsesLightTheme();
        ApplyTheme(_currentIsLight);
        Tokens.ValidateAll();   // ONLY AFTER the theme slot is filled — otherwise every
                                // theme-dependent key throws on the first start.
    }

    /// Every window that wants its title bar coloured registers itself — CorePin is a tray
    /// tool, its window may exist long before or long after a theme change.
    public void RegisterWindow(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            _registeredWindows.Add(window);
            ApplyTitleBar(window, _currentIsLight);   // on the current theme right away,
        };                                            // not only at the next change
        window.Closed += (_, _) => _registeredWindows.Remove(window);
    }

    /// Call target for S08 (S03 §6.2), UI thread only.
    public void OnSystemThemeChanged()
    {
        bool isLight = ThemeSettings.ReadAppUsesLightTheme();
        if (isLight == _currentIsLight) return;      // no redraw without a real change
        ApplyTheme(isLight);
    }

    private void ApplyTheme(bool isLight)
    {
        _themeSlot.MergedDictionaries.Clear();
        _themeSlot.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(isLight ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative)
        });
        foreach (var window in _registeredWindows) ApplyTitleBar(window, isLight);
        _currentIsLight = isLight;
        ThemeApplied?.Invoke();
    }

    private static void ApplyTitleBar(Window window, bool isLight)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        TitleBarTheme.SetDark(hwnd, dark: !isLight);
    }
}
