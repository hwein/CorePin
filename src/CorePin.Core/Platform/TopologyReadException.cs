namespace CorePin.Core.Platform;

/// S01 §3.3 / S04 §3.6. Belongs to the port, not to the format — the format error is
/// TopologyFormatException in CorePin.Core.Topology.
public sealed class TopologyReadException : Exception
{
    public TopologyReadException(string message, int win32Error = 0)
        : base(message) => Win32Error = win32Error;

    public TopologyReadException(string message, Exception innerException)
        : base(message, innerException) { }

    /// 0 = no Win32 error involved.
    public int Win32Error { get; }
}
