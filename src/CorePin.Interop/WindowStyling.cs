namespace CorePin.Interop;

/// Public shell around the internal NativeMethods declarations, which CorePin.App cannot reach.
public static class WindowStyling
{
    /// Callable only once the HWND exists; resizing stays, only maximizing goes.
    public static bool DisableMaximize(nint hwnd)
    {
        if (hwnd == 0) return false;

        nint style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        NativeMethods.SetWindowLongPtr(
            hwnd, NativeMethods.GWL_STYLE, style & ~(nint)NativeMethods.WS_MAXIMIZEBOX);

        // SetWindowLongPtr returns the previous style, so only a readback tells whether it took.
        return (NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE)
                & (nint)NativeMethods.WS_MAXIMIZEBOX) == 0;
    }
}
