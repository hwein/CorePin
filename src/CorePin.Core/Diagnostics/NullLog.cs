namespace CorePin.Core.Diagnostics;

public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();

    private NullLog() { }

    public bool IsEnabled(LogLevel level) => false;

    public void Write(LogLevel level, string category, string message) { }
}
