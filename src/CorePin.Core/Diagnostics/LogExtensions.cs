namespace CorePin.Core.Diagnostics;

public static class LogExtensions
{
    public static void Debug(this ILog log, string category, string message)
        => log.Write(LogLevel.Debug, category, message);

    public static void Info(this ILog log, string category, string message)
        => log.Write(LogLevel.Info, category, message);

    public static void Warn(this ILog log, string category, string message)
        => log.Write(LogLevel.Warn, category, message);
}
