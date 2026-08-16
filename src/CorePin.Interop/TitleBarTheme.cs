namespace CorePin.Interop;

/// Public shell around the internal NativeMethods declaration — ThemeController lives in
/// CorePin.App and cannot reach NativeMethods directly (S03 §6.3).
public static class TitleBarTheme
{
    /// Paints the native title bar of hwnd dark or light
    /// (DWMWA_USE_IMMERSIVE_DARK_MODE). Callable only once the HWND exists.
    public static void SetDark(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;
        int value = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, in value, sizeof(int));
    }
}
