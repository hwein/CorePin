using System.Runtime.CompilerServices;
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

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true,
                   StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? lpModuleName);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(in WNDCLASSEXW lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true,
                   StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true,
                   StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    internal const uint WM_NULL = 0x0000;
    internal const uint WM_SETTINGCHANGE = 0x001A;
    internal const uint WM_COPYDATA = 0x004A;
    internal const uint WM_CONTEXTMENU = 0x007B;
    internal const uint WM_DPICHANGED = 0x02E0;
    internal const uint WS_OVERLAPPED = 0x00000000;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ChangeWindowMessageFilterEx(
        nint hwnd, uint message, uint action, nint pChangeFilterStruct);

    internal const uint MSGFLT_ALLOW = 1;

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", SetLastError = true,
                   StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindow(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint dwProcessId);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static partial nint SendMessageTimeout(
        nint hWnd, uint msg, nint wParam, in COPYDATASTRUCT lParam,
        uint fuFlags, uint uTimeout, out nuint lpdwResult);

    internal const uint SMTO_ABORTIFHUNG = 0x0002;

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Shell_NotifyIcon(uint dwMessage, in NOTIFYICONDATAW lpData);

    internal const uint NIM_ADD = 0x00000000;
    internal const uint NIM_MODIFY = 0x00000001;
    internal const uint NIM_DELETE = 0x00000002;
    internal const uint NIM_SETVERSION = 0x00000004;

    internal const uint NIF_MESSAGE = 0x00000001;
    internal const uint NIF_ICON = 0x00000002;
    internal const uint NIF_TIP = 0x00000004;
    internal const uint NIF_SHOWTIP = 0x00000080;

    internal const uint NOTIFYICON_VERSION_4 = 4;
    internal const uint NIN_SELECT = 0x0400;            // WM_USER + 0
    internal const uint NIN_KEYSELECT = 0x0401;         // NIN_SELECT | NINF_KEY

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", SetLastError = true,
                   StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    /// Not a BOOL here: under TPM_RETURNCMD the return value is the chosen command id.
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int TrackPopupMenu(
        nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    internal const uint MF_STRING = 0x00000000;
    internal const uint MF_SEPARATOR = 0x00000800;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_RIGHTALIGN = 0x0008;
    internal const uint TPM_BOTTOMALIGN = 0x0020;
    internal const uint TPM_RETURNCMD = 0x0100;

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint CreateIconFromResourceEx(
        nint presbits, uint dwResSize, [MarshalAs(UnmanagedType.Bool)] bool fIcon, uint dwVer,
        int cxDesired, int cyDesired, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);

    internal const int SM_CXSMICON = 49;
    internal const uint ICON_RESOURCE_VERSION = 0x00030000;   // the .ico image format version
    internal const uint LR_DEFAULTCOLOR = 0x00000000;
}

/// Kept alive by the window that installs it: Win32 holds only the native pointer.
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

[StructLayout(LayoutKind.Sequential)]
internal struct FILETIME
{
    public uint DwLowDateTime;
    public uint DwHighDateTime;

    public readonly long ToFileTimeUtc() => ((long)DwHighDateTime << 32) | DwLowDateTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct COPYDATASTRUCT
{
    public nuint DwData;
    public int CbData;
    public nint LpData;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WNDCLASSEXW
{
    public uint CbSize;
    public uint Style;
    public nint LpfnWndProc;
    public int CbClsExtra;
    public int CbWndExtra;
    public nint HInstance;
    public nint HIcon;
    public nint HCursor;
    public nint HbrBackground;
    public nint LpszMenuName;
    public nint LpszClassName;
    public nint HIconSm;
}

/// UTF-16 code units as ushort: a char field would make the struct non-blittable (SYSLIB1051).
[InlineArray(128)]
internal struct Char128 { private ushort _element0; }

[InlineArray(256)]
internal struct Char256 { private ushort _element0; }

[InlineArray(64)]
internal struct Char64 { private ushort _element0; }

[StructLayout(LayoutKind.Sequential)]
internal struct NOTIFYICONDATAW
{
    public uint CbSize;
    public nint HWnd;
    public uint UID;
    public uint UFlags;
    public uint UCallbackMessage;
    public nint HIcon;
    public Char128 SzTip;
    public uint DwState;
    public uint DwStateMask;
    public Char256 SzInfo;
    public uint UVersion;                  // shares its four bytes with uTimeout
    public Char64 SzInfoTitle;
    public uint DwInfoFlags;
    public Guid GuidItem;
    public nint HBalloonIcon;

    /// szTip holds 128 code units including the terminator, so at most 127 characters fit.
    public void SetTip(string text)
    {
        Span<ushort> buffer = SzTip;
        var tip = MemoryMarshal.Cast<ushort, char>(buffer);
        tip.Clear();
        text.AsSpan(0, Math.Min(text.Length, tip.Length - 1)).CopyTo(tip);
    }
}
