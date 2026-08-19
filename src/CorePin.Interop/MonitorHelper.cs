using System.Runtime.InteropServices;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// Window geometry in device independent pixels — the same unit WPF's Left/Top/Width/Height use.
public readonly record struct DipRect(double X, double Y, double Width, double Height);

/// The two monitor questions WPF cannot answer itself: is a stored rectangle still on a screen,
/// and where is the work area of the screen holding the cursor.
public static class MonitorHelper
{
    private const double DefaultDpi = 96.0;

    /// Converts with the system DPI factor: exact under uniform scaling, off on any monitor scaled differently from the system — harmless, at worst an unneeded re-centre.
    public static bool IntersectsAnyMonitor(DipRect bounds)
    {
        double scale = GetDpiForSystem() / DefaultDpi;
        var rect = new RECT
        {
            Left = (int)Math.Floor(bounds.X * scale),
            Top = (int)Math.Floor(bounds.Y * scale),
            Right = (int)Math.Ceiling((bounds.X + bounds.Width) * scale),
            Bottom = (int)Math.Ceiling((bounds.Y + bounds.Height) * scale),
        };
        return MonitorFromRect(in rect, MONITOR_DEFAULTTONULL) != 0;
    }

    /// Converts with that monitor's own DPI factor, not the system one: a first start centred
    /// with the wrong factor would open visibly too large or too small.
    public static DipRect WorkAreaAtCursor()
    {
        // A locked session or the secure desktop is a documented GetCursorPos failure: fall back to the primary monitor instead of throwing. Neither MONITOR_DEFAULTTONEAREST nor _TOPRIMARY ever returns NULL.
        nint monitor = GetCursorPos(out var cursor)
            ? MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST)
            : MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);

        var info = new MONITORINFO { CbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) throw Failed("GetMonitorInfoW");

        int hr = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
        if (hr != 0)
            throw new InvalidOperationException($"GetDpiForMonitor failed: HRESULT 0x{hr:X8}");

        double scaleX = dpiX / DefaultDpi;
        double scaleY = dpiY / DefaultDpi;
        var work = info.RcWork;
        return new DipRect(work.Left / scaleX, work.Top / scaleY,
                           (work.Right - work.Left) / scaleX, (work.Bottom - work.Top) / scaleY);
    }

    private static InvalidOperationException Failed(string api)
        => new($"{api} failed: {Marshal.GetLastPInvokeError()}");
}
