using System.Runtime.InteropServices;
using System.Text;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// One instance per logon session, and a way for the second one to wake the first.
public static class SingleInstance
{
    private const string MutexName = @"Local\CorePin.SingleInstance";
    private const int MaxAttempts = 20;
    private const int RetryDelayMs = 200;                    // 20 x 200 ms = 4 s
    private const uint SendTimeoutMs = 2000;

    public static bool TryAcquire(out Mutex mutex)
    {
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (createdNew) return true;
            mutex.Dispose();
        }
        catch (UnauthorizedAccessException)
        {
            // Same answer as an occupied mutex: SignalExisting stays honest either way.
        }

        // Contract: on false the caller never reads mutex, the return value is the only truth.
        mutex = null!;
        return false;
    }

    public static void SignalExisting()
    {
        nint hwnd = 0;
        for (int attempt = 1; attempt <= MaxAttempts && hwnd == 0; attempt++)
        {
            hwnd = FindWindow(MessageWindow.ClassName, null);
            if (hwnd == 0) Thread.Sleep(RetryDelayMs);
        }

        if (hwnd != 0 && Deliver(hwnd)) return;
        MessageBoxes.ShowSecondInstanceUnreachable();         // never disappear without a word
    }

    private static bool Deliver(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint firstInstancePid);
        AllowSetForegroundWindow(firstInstancePid);           // without it the first window only blinks

        byte[] payload = Encoding.Unicode.GetBytes("show");
        var pin = GCHandle.Alloc(payload, GCHandleType.Pinned);
        try
        {
            var data = new COPYDATASTRUCT
            {
                DwData = 1,
                CbData = payload.Length,
                LpData = pin.AddrOfPinnedObject(),
            };
            return SendMessageTimeout(hwnd, WM_COPYDATA, 0, in data,
                                      SMTO_ABORTIFHUNG, SendTimeoutMs, out _) != 0;
        }
        finally { pin.Free(); }
    }
}
