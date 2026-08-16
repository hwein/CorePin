namespace CorePin.Core.Diagnostics;

/// Implementations must be thread-safe (S01 §3.1): the UI thread, the engine worker
/// thread and the startup path call this without knowing about each other.
public interface ILog
{
    bool IsEnabled(LogLevel level);
    void Write(LogLevel level, string category, string message);
}
