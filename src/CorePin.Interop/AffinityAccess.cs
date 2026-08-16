using System.Runtime.InteropServices;
using CorePin.Core.Platform;

namespace CorePin.Interop;

public sealed class AffinityAccess : IAffinityAccess
{
    public ProcessOpenResult Open(int pid)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION | NativeMethods.PROCESS_SET_INFORMATION,
            bInheritHandle: false, (uint)pid);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();   // handle.IsInvalid is managed, does not disturb it
            var failure = Win32ErrorMapping.ClassifyOpenFailure(error);
            handle.Dispose();
            return new ProcessOpenResult(null, failure, error);
        }
        return new ProcessOpenResult(new ProcessAccessHandle(handle), OpenFailure.None, 0);
    }
}
