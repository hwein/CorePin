using Microsoft.Win32.SafeHandles;

namespace CorePin.Interop;

internal sealed class SafeProcessAccessHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeProcessAccessHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
