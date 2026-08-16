namespace CorePin.Core.Diagnostics;

public static class LogExtensions
{
    public static void Trace(this ILog log, string category, string message)
        => log.Write(LogLevel.Trace, category, message);

    public static void Debug(this ILog log, string category, string message)
        => log.Write(LogLevel.Debug, category, message);

    public static void Information(this ILog log, string category, string message)
        => log.Write(LogLevel.Information, category, message);

    public static void Warning(this ILog log, string category, string message)
        => log.Write(LogLevel.Warning, category, message);

    public static void Error(this ILog log, string category, string message)
        => log.Write(LogLevel.Error, category, message);

    public static void Critical(this ILog log, string category, string message)
        => log.Write(LogLevel.Critical, category, message);
}
