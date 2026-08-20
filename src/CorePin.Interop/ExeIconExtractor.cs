using Microsoft.Win32.SafeHandles;

namespace CorePin.Interop;

/// Public: instances travel to CorePin.App, which disposes them after the conversion.
public sealed class SafeIconHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeIconHandle(nint handle) : base(ownsHandle: true) => SetHandle(handle);

    protected override bool ReleaseHandle() => NativeMethods.DestroyIcon(handle);
}

public static class ExeIconExtractor
{
    /// One call extracts and answers "does this file carry an icon at all"; null for both
    /// no resource and unreadable file, which the rule list shows identically.
    public static SafeIconHandle? Extract(string exePath, int size = 32)
    {
        ArgumentNullException.ThrowIfNull(exePath);

        Span<nint> handles = stackalloc nint[1];
        uint extracted = NativeMethods.PrivateExtractIcons(
            exePath, nIconIndex: 0, cxIcon: size, cyIcon: size,
            phicon: handles, piconid: 0, nIcons: 1, flags: 0);

        // 0xFFFFFFFF means "file not found" and would otherwise read as a huge success.
        if (extracted == 0 || extracted == uint.MaxValue) return null;

        var handle = new SafeIconHandle(handles[0]);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }
}
