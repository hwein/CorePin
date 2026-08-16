using System.Runtime.InteropServices;
using CorePin.Core.Platform;
using CorePin.Core.Primitives;

namespace CorePin.Interop;

internal sealed class ProcessAccessHandle : IProcessHandle
{
    private readonly SafeProcessAccessHandle _handle;

    internal ProcessAccessHandle(SafeProcessAccessHandle handle) => _handle = handle;

    public int LastError { get; private set; }

    public string? QueryExeName()
    {
        Span<char> buffer = stackalloc char[512];
        uint size = (uint)buffer.Length;
        bool ok = NativeMethods.QueryFullProcessImageName(_handle, 0, buffer, ref size);
        LastError = ok ? 0 : Marshal.GetLastPInvokeError();
        if (!ok) return null;
        return Path.GetFileName(buffer[..(int)size].ToString());
    }

    public DateTime? QueryStartTimeUtc()
    {
        bool ok = NativeMethods.GetProcessTimes(
            _handle, out var creation, out _, out _, out _);
        LastError = ok ? 0 : Marshal.GetLastPInvokeError();
        if (!ok) return null;
        return DateTime.FromFileTimeUtc(creation.ToFileTimeUtc());
    }

    public AffinityPair? GetAffinity()
    {
        bool ok = NativeMethods.GetProcessAffinityMask(
            _handle, out nuint process, out nuint system);
        LastError = ok ? 0 : Marshal.GetLastPInvokeError();
        if (!ok) return null;
        return new AffinityPair(MaskConversion.ToCore(process), MaskConversion.ToCore(system));
    }

    public bool SetAffinity(AffinityMask mask)
    {
        bool ok = NativeMethods.SetProcessAffinityMask(_handle, MaskConversion.ToNative(mask));
        LastError = ok ? 0 : Marshal.GetLastPInvokeError();
        return ok;
    }

    public void Dispose() => _handle.Dispose();
}
