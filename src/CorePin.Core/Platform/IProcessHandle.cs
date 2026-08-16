using CorePin.Core.Primitives;

namespace CorePin.Core.Platform;

public interface IProcessHandle : IDisposable
{
    /// QueryFullProcessImageName, shortened to the file name.
    string? QueryExeName();

    /// GetProcessTimes over the same handle.
    DateTime? QueryStartTimeUtc();

    /// null on failure; the Win32 error is available via LastError.
    AffinityPair? GetAffinity();

    bool SetAffinity(AffinityMask mask);

    int LastError { get; }
}
