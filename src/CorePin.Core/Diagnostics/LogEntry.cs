namespace CorePin.Core.Diagnostics;

internal readonly record struct LogEntry(
    DateTime TimestampUtc, LogLevel Level, string Category, string Message, bool Forced = false);
