namespace CorePin.Interop;

/// Deliberately opens with PROCESS_QUERY_LIMITED_INFORMATION alone: asking for the
/// combined rights of IAffinityAccess.Open loses even read access on protected processes.
public static class ProcessPathResolver
{
    /// The extended path limit — a shorter buffer would fail the call instead of truncating.
    private const int PathBufferChars = 32768;

    /// null on every failure: a denied open is the expected outcome for many processes.
    public static string? TryResolveExePath(int pid)
    {
        using var handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, bInheritHandle: false, (uint)pid);
        if (handle.IsInvalid) return null;

        var buffer = new char[PathBufferChars];
        uint size = (uint)buffer.Length;
        if (!NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size)) return null;

        return new string(buffer, 0, (int)size);
    }
}
