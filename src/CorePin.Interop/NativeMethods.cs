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

    // ── kernel32.dll: topology (S04 §2.1 — the one authoritative declaration) ────────

    /// The buffer is passed as nint, not byte*: nint is a blittable, pointer-sized
    /// integer, so neither the declaration nor the walk needs unsafe (S04 T24).
    [LibraryImport("kernel32.dll", EntryPoint = "GetLogicalProcessorInformationEx",
                   SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetLogicalProcessorInformationEx(
        LogicalProcessorRelationship relationshipType, nint buffer, ref uint returnedLength);

    internal enum LogicalProcessorRelationship : uint
    {
        RelationProcessorCore = 0, RelationNumaNode = 1, RelationCache = 2,
        RelationProcessorPackage = 3, RelationGroup = 4, RelationProcessorDie = 5,
        RelationNumaNodeEx = 6, RelationProcessorModule = 7, RelationAll = 0xFFFF,
    }

    internal const byte LTP_PC_SMT = 0x1;               // bit 0 in PROCESSOR_RELATIONSHIP.Flags

    // ── kernel32.dll: console attachment for --dump-topology (S01 §6.4, S07 §14.7) ───

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachConsole(uint dwProcessId);

    internal const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    /// Deliberately no SafeHandle return type: standard handles belong to the console
    /// subsystem, not to CorePin — there is nothing to own and nothing to close (S07 §14.7).
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GetStdHandle(int nStdHandle);

    internal const int STD_OUTPUT_HANDLE = -11;

    // ── user32.dll: WPF-free abort paths (S07 §8.3/§14.6) ───────────────────────────

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", SetLastError = true,
                   StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(nint hWnd, string text, string caption, uint type);

    internal const uint MB_OK = 0x00000000;
    internal const uint MB_ICONWARNING = 0x00000030;
    internal const uint MB_ICONINFORMATION = 0x00000040;
}
