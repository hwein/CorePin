using System.Runtime.InteropServices;

namespace CorePin.Interop;

/// The single auditable place for native declarations — grouped by DLL, no logic here.
internal static partial class NativeMethods
{

    /// Valid from Windows 10 build 18985; the target platform starts at 19044, so no branch.
    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, in int pvAttribute, int cbAttribute);

    /// nint, not byte*: blittable and pointer-sized, so nothing here needs unsafe.
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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachConsole(uint dwProcessId);

    internal const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    /// No SafeHandle: standard handles belong to the console subsystem, nothing to close.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GetStdHandle(int nStdHandle);

    internal const int STD_OUTPUT_HANDLE = -11;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", SetLastError = true,
                   StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(nint hWnd, string text, string caption, uint type);

    internal const uint MB_OK = 0x00000000;
    internal const uint MB_ICONWARNING = 0x00000030;
    internal const uint MB_ICONINFORMATION = 0x00000040;

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint PROCESS_SET_INFORMATION = 0x0200;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial SafeProcessAccessHandle OpenProcess(
        uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true,
                   StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryFullProcessImageName(
        SafeProcessAccessHandle hProcess, uint dwFlags, Span<char> lpExeName, ref uint lpdwSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcessTimes", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(
        SafeProcessAccessHandle hProcess,
        out FILETIME lpCreationTime, out FILETIME lpExitTime,
        out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessAffinityMask(
        SafeProcessAccessHandle hProcess, out nuint lpProcessAffinityMask, out nuint lpSystemAffinityMask);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessAffinityMask(
        SafeProcessAccessHandle hProcess, nuint dwProcessAffinityMask);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FILETIME
{
    public uint DwLowDateTime;
    public uint DwHighDateTime;

    public readonly long ToFileTimeUtc() => ((long)DwHighDateTime << 32) | DwLowDateTime;
}
