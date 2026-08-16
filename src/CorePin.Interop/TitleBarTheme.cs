namespace CorePin.Interop;

/// Public shell around the internal NativeMethods declaration, which CorePin.App cannot reach.
public static class TitleBarTheme
{
    /// Callable only once the HWND exists.
    public static void SetDark(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;
        int value = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, in value, sizeof(int));
    }
}
