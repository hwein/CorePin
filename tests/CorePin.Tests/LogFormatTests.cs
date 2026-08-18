using System.Globalization;
using CorePin.Core.Diagnostics;

namespace CorePin.Tests;

public static class LogFormatTests
{
    private static readonly DateTime Stamp = new(2026, 8, 15, 9, 12, 3, 1, DateTimeKind.Utc);

    public static void Test_Format_FixedColumnsAndTimestampLength()
    {
        string line = FileLog.FormatLine(new LogEntry(Stamp, LogLevel.Information, "app", "hello"));

        Assert.Equal("2026-08-15T09:12:03.001Z", line[..24], "timestamp, 24 characters");
        Assert.Equal(' ', line[24], "separator after the timestamp");
        Assert.Equal("INFO ", line.Substring(25, 5), "level, 5 characters");
        Assert.Equal("app     ", line.Substring(31, 8), "category, field width 8");
        Assert.Equal("hello", line[40..], "text from column 41 on");
    }

    public static void Test_Format_LevelAbbreviationsAreFiveChars()
    {
        Assert.Equal("TRACE", FileLog.FormatLine(new LogEntry(Stamp, LogLevel.Trace, "app", "x")).Substring(25, 5),
            "TRACE");
        Assert.Equal("DEBUG", FileLog.FormatLine(new LogEntry(Stamp, LogLevel.Debug, "app", "x")).Substring(25, 5),
            "DEBUG");
        Assert.Equal("INFO ", FileLog.FormatLine(new LogEntry(Stamp, LogLevel.Information, "app", "x")).Substring(25, 5),
            "INFO with padding");
        Assert.Equal("WARN ", FileLog.FormatLine(new LogEntry(Stamp, LogLevel.Warning, "app", "x")).Substring(25, 5),
            "WARN with padding");
        Assert.Equal("ERROR", FileLog.FormatLine(new LogEntry(Stamp, LogLevel.Error, "app", "x")).Substring(25, 5),
            "ERROR");
        Assert.Equal("CRIT ", FileLog.FormatLine(new LogEntry(Stamp, LogLevel.Critical, "app", "x")).Substring(25, 5),
            "CRIT with padding, CRITICAL would not fit the five-character column");
    }

    public static void Test_Format_InvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            string line = FileLog.FormatLine(new LogEntry(Stamp, LogLevel.Information, "app", "x"));
            Assert.Equal("2026-08-15T09:12:03.001Z", line[..24],
                "th-TH (Buddhist calendar) yields the same 24 characters");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    public static void Test_Format_EmbeddedLineBreaksAreEscaped()
    {
        string line = FileLog.FormatLine(new LogEntry(Stamp, LogLevel.Information, "app", "a\r\nb\nc\rd"));
        Assert.Equal(@"a\nb\nc\nd", line[40..], "embedded line breaks become \\n");
        Assert.True(!line.Contains('\n', StringComparison.Ordinal), "no real line break inside the line");
    }
}
