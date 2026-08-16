using System.Runtime.InteropServices;

namespace CorePin.Interop;

/// The single auditable place for native declarations (S01 §2.2, boundary 3).
/// Grouped by DLL; no logic here.
internal static partial class NativeMethods
{
    // ── dwmapi.dll ──────────────────────────────────────────────────────────────────

    /// Valid from Windows 10 build 18985 on; the target platform starts at 19044,
    /// so no version branch is needed (S03 §6.3).
    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, in int pvAttribute, int cbAttribute);
}
