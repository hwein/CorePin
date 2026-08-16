using System.Diagnostics;
using CorePin.Core.Diagnostics;
using CorePin.Tests.Fakes;

namespace CorePin.Tests;

public static class FileLogTests
{
    public static void Test_Rolling_NewFilePerCreate()
    {
        using var dir = new TempDir();
        using (var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock()))
        {
            log.Minimum = LogLevel.Information;
            log.Information("app", "one");
        }

        Assert.Equal(1, LogFiles(dir.Path).Length, "exactly one log file per program start (S02 §3.2)");
        Assert.Equal("CorePin_20260815_091203Z.log", Path.GetFileName(LogFiles(dir.Path)[0]),
            "file name CorePin_yyyyMMdd_HHmmssZ.log, UTC");
    }

    public static void Test_Rolling_CollisionGetsSuffix()
    {
        using var dir = new TempDir();
        var log1 = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock());
        log1.Flush();
        var log2 = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock());
        log2.Flush();
        log1.Dispose();
        log2.Dispose();

        var names = LogFiles(dir.Path).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal("CorePin_20260815_091203Z-2.log|CorePin_20260815_091203Z.log", string.Join("|", names),
            "a collision gets the suffix -2 (S02 §3.2)");
    }

    /// The 10 MB of production would need a multi-minute test, so the seam sets a few
    /// hundred bytes — which is why the header reports "(0 MB)" here: the number is
    /// computed from the limit in force, and that limit is below one megabyte.
    public static void Test_Rolling_SplitsOnSizeLimit()
    {
        using var dir = new TempDir();
        var limits = FileLogLimits.Default with { FileSizeBytes = 600 };
        using (var log = FileLog.CreateForTests(dir.Path, LogLevel.Information, new FakeClock(), limits))
        {
            log.Minimum = LogLevel.Information;
            for (int i = 0; i < 20; i++) log.Information("app", "0123456789");
        }

        var files = LogFiles(dir.Path);
        Assert.Equal(2, files.Length, "over the size limit a continuation file is opened (S02 §3.3)");
        Assert.Equal("CorePin_20260815_091203Z_part2.log", Path.GetFileName(files[1]),
            "the continuation is named _part2");
        Assert.Equal("log file size limit reached (0 MB), continued from CorePin_20260815_091203Z.log",
            MessagesOf(files[1])[0],
            "the continuation opens with the warn header carrying the previous FILE NAME (S02 §7.1)");
        Assert.Equal("WARN ", File.ReadAllText(files[1]).Substring(25, 5),
            "the continuation header is a warn line (app.file-limit-reached)");
    }

    public static void Test_Rolling_SessionLimitSparesOriginFile()
    {
        using var dir = new TempDir();
        var limits = FileLogLimits.Default with { FileSizeBytes = 600, SessionFiles = 3 };
        using (var log = FileLog.CreateForTests(dir.Path, LogLevel.Information, new FakeClock(), limits))
        {
            log.Minimum = LogLevel.Information;
            for (int i = 0; i < 200; i++) log.Information("app", "0123456789");
        }

        var names = LogFiles(dir.Path).Select(Path.GetFileName).ToArray();
        Assert.Equal(3, names.Length, "the session never holds more files than the session limit (S02 §3.3)");
        Assert.True(names.Contains("CorePin_20260815_091203Z.log"),
            "the origin file is never the one deleted — it carries the session header (S02 §3.3)");
        Assert.True(!names.Contains("CorePin_20260815_091203Z_part2.log"),
            "the OLDEST continuation is the one deleted");
        Assert.True(LogFiles(dir.Path).Any(f => File.ReadAllText(f)
                        .Contains("session log size limit reached (3 files), oldest continuation file removed",
                                  StringComparison.Ordinal)),
            "the deletion is reported (app.session-limit-reached)");
    }

    public static void Test_Rolling_RunningSessionExemptFromDirectoryBudget()
    {
        using var dir = new TempDir();
        string foreign = Path.Combine(dir.Path, "CorePin_20200101_000000Z.log");
        File.WriteAllText(foreign, new string('x', 2000));

        var limits = FileLogLimits.Default with { FileSizeBytes = 600, DirectoryBudgetBytes = 100 };
        using (var log = FileLog.CreateForTests(dir.Path, LogLevel.Information, new FakeClock(), limits))
        {
            log.Minimum = LogLevel.Information;
            for (int i = 0; i < 60; i++) log.Information("app", "0123456789");
        }

        Assert.True(!File.Exists(foreign), "a foreign, older session prefix is deleted over budget (S02 §3.3)");

        var session = LogFiles(dir.Path);
        Assert.True(session.Length >= 2, $"the session split into several files, got: {session.Length}");
        Assert.True(session.Sum(f => new FileInfo(f).Length) > limits.DirectoryBudgetBytes,
            "the running session stays above the directory budget and is not touched by it");
    }

    public static void Test_LevelFiltering_DebugInformationWarning()
    {
        using var dir = new TempDir();
        using (var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock()))
        {
            log.Minimum = LogLevel.Warning;
            log.Debug("app", "d");
            log.Information("app", "i");
            log.Warning("app", "w");
        }

        Assert.Equal("w", string.Join("|", Messages(dir.Path)), "only lines >= Minimum are written");
    }

    public static void Test_ColdStart_IsEnabledIsOpen()
    {
        using var dir = new TempDir();
        using var log = FileLog.Create(dir.Path, LogLevel.Warning, new FakeClock());

        Assert.True(log.IsEnabled(LogLevel.Debug), "before the first Minimum assignment every level is open (S02 §5.4)");
        log.Minimum = LogLevel.Warning;
        Assert.True(!log.IsEnabled(LogLevel.Debug), "after that the level filter applies");
    }

    public static void Test_ColdStartBuffer_Replay()
    {
        using var dir = new TempDir();
        using (var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock()))
        {
            log.Information("app", "before");
            log.Minimum = LogLevel.Information;
            log.Information("app", "after");
        }

        Assert.Equal("before|after", string.Join("|", Messages(dir.Path)),
            "buffered lines appear in order ahead of the later ones (S02 §9.3)");
    }

    public static void Test_ColdStartBuffer_ReplayDropsBelowNewMinimum()
    {
        using var dir = new TempDir();
        using (var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock()))
        {
            log.Debug("app", "d");
            log.Warning("app", "w");
            log.Minimum = LogLevel.Warning;
        }

        Assert.Equal("w", string.Join("|", Messages(dir.Path)),
            "the replay filters against the Minimum set by then");
    }

    public static void Test_ColdStartBuffer_WriteAlwaysSurvivesEveryLevel()
    {
        using var dir = new TempDir();
        using (var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock()))
        {
            log.WriteAlways(LogLevel.Information, "app", "CorePin 0.1.0 starting (flags: none)");
            log.Minimum = LogLevel.Warning;
        }

        Assert.Equal("CorePin 0.1.0 starting (flags: none)", string.Join("|", Messages(dir.Path)),
            "Forced survives Minimum = Warning (S02 §6, †)");
    }

    public static void Test_ColdStartBuffer_LimitDropsYoungestLines()
    {
        using var dir = new TempDir();
        using (var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock()))
        {
            for (int i = 0; i < 250; i++) log.Information("app", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            log.Minimum = LogLevel.Information;
        }

        var messages = Messages(dir.Path);
        Assert.Equal(200, messages.Length, "the cold start buffer holds 200 lines (S02 §9.3)");
        Assert.Equal("0", messages[0], "the oldest lines stay");
        Assert.Equal("199", messages[^1], "the youngest are dropped");
    }

    public static void Test_ColdStartBuffer_ForcedReplayOnDispose()
    {
        using var dir = new TempDir();
        using (var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock()))
        {
            log.Information("app", "never-set-minimum");
        }

        Assert.Equal("never-set-minimum", string.Join("|", Messages(dir.Path)),
            "Dispose forces an open cold start buffer to drain (S02 §5.6)");
    }

    public static void Test_ColdStartBuffer_ConcurrentWriting()
    {
        using var dir = new TempDir();
        using (var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock()))
        {
            var threads = new List<Thread>();
            for (int t = 0; t < 4; t++)
            {
                var thread = new Thread(() => { for (int i = 0; i < 50; i++) log.Information("app", "x"); });
                threads.Add(thread);
                thread.Start();
            }
            log.Minimum = LogLevel.Information;
            foreach (var thread in threads) thread.Join();
        }

        var messages = Messages(dir.Path);
        Assert.True(messages.Length == 200, $"no loss in the replay race, got: {messages.Length}");
    }

    public static void Test_Queue_OverflowDropsLinesAndReports()
    {
        using var dir = new TempDir();
        using var gate = new ManualResetEventSlim(false);
        var limits = FileLogLimits.Default with { QueueCapacity = 10 };

        var log = FileLog.CreateForTests(dir.Path, LogLevel.Information, new FakeClock(), limits, gate);
        log.Minimum = LogLevel.Information;
        for (int i = 0; i < 30; i++) log.Information("app", "line");   // the writer is still held back

        gate.Set();
        log.Dispose();

        var messages = Messages(dir.Path);
        Assert.Equal(11, messages.Length, "ten queued lines plus the overflow report (S02 §5.3)");
        Assert.Equal("log queue overflow, 20 lines dropped since last report", messages[^1],
            "the writer reports the overflow itself (app.queue-overflow)");
    }

    public static void Test_Queue_WriteAfterDisposeHasNoEffect()
    {
        using var dir = new TempDir();
        var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock());
        log.Minimum = LogLevel.Information;
        log.Dispose();

        log.Information("app", "after dispose");   // does not throw
        log.Flush();

        Assert.Equal(0, Messages(dir.Path).Length, "after Dispose nothing is written any more (S02 §10)");
    }

    public static void Test_Shutdown_QueueDrainsBeforeClosing()
    {
        using var dir = new TempDir();
        using (var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock()))
        {
            log.Minimum = LogLevel.Information;
            for (int i = 0; i < 500; i++) log.Information("app", "line");
        }

        Assert.Equal(500, Messages(dir.Path).Length, "Dispose waits for the queue to drain (S02 §5.6)");
    }

    public static void Test_ErrorHandling_DirectoryFallback()
    {
        using var dir = new TempDir();
        string blocker = Path.Combine(dir.Path, "blocker");
        File.WriteAllText(blocker, "not a directory");
        string impossible = Path.Combine(blocker, "logs");

        string expected = Path.Combine(Path.GetTempPath(), "CorePin", "logs");
        string? created;
        using (var log = FileLog.Create(impossible, LogLevel.Information, new FakeClock()))
        {
            Assert.Equal(expected, log.Directory, "fallback to %TEMP% (S02 §10)");
            log.Minimum = LogLevel.Information;
            log.Flush();
            created = LogFiles(log.Directory).FirstOrDefault();
        }

        // Best effort clean-up outside the TempDir: a locked or already removed file is no
        // reason to fail the test, the assertion above is already done.
        if (created is not null) { try { File.Delete(created); } catch (IOException) { } }
    }

    public static void Test_SessionHeader_AppearsRegardlessOfMinimum()
    {
        using var dir = new TempDir();
        using (var log = FileLog.Create(dir.Path, LogLevel.Warning, new FakeClock()))
        {
            log.Minimum = LogLevel.Warning;
            log.WriteAlways(LogLevel.Information, "app", "CorePin 0.1.0 starting (flags: none)");
            log.Information("app", "swallowed");
            log.WriteAlways(LogLevel.Information, "app", "CorePin exiting (code 0)");
        }

        Assert.Equal("CorePin 0.1.0 starting (flags: none)|CorePin exiting (code 0)",
            string.Join("|", Messages(dir.Path)),
            "app.start and app.exit are there even at Minimum = Warning (S02 §6)");
    }

    public static void Test_NonBlocking_TenThousandCalls()
    {
        using var dir = new TempDir();
        using var log = FileLog.Create(dir.Path, LogLevel.Information, new FakeClock());
        log.Minimum = LogLevel.Information;

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++) log.Information("app", "throughput");
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < 2000,
            $"10000 calls do not block, measured: {watch.ElapsedMilliseconds} ms");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────

    private static string[] LogFiles(string directory)
        => Directory.Exists(directory)
            ? Directory.GetFiles(directory, "CorePin_*.log").OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : [];

    /// Message column of the origin file, in order.
    private static string[] Messages(string directory)
    {
        var files = LogFiles(directory);
        return files.Length == 0 ? [] : MessagesOf(files[0]);
    }

    /// Message column of one named file, in order.
    private static string[] MessagesOf(string file)
        => File.ReadAllText(file)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Length > 40 ? line[40..] : line)
            .ToArray();
}
