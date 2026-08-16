namespace CorePin.Core.Diagnostics;

/// Implementations must be thread-safe: UI thread, worker thread and startup all call it.
public interface ILog
{
    bool IsEnabled(LogLevel level);
    void Write(LogLevel level, string category, string message);
}
