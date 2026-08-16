using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using CorePin.Core.Time;

namespace CorePin.Core.Diagnostics;

internal enum QueueItemKind { Line, SyncMarker }

internal readonly record struct QueueItem(
    QueueItemKind Kind, LogEntry? Line = null, ManualResetEventSlim? Signal = null);

/// One background writer, one queue. Write never blocks and never throws.
public sealed class FileLog : ILog, IDisposable
{
    private const int JoinTimeoutMs = 1000;
    private const int FlushTimeoutMs = 250;

    private readonly IClock _clock;
    private readonly BlockingCollection<QueueItem> _queue;
    private readonly ManualResetEventSlim? _writerGate;
    private readonly Thread _writer;
    private readonly ColdStartGate _gate;
    private readonly LogFileWriter _files;

    private volatile bool _closed;
    private int _dropped;

    private FileLog(string directory, LogLevel minimum, IClock clock,
                    FileLogLimits limits, ManualResetEventSlim? writerGate)
    {
        _clock = clock;
        _queue = new BlockingCollection<QueueItem>(limits.QueueCapacity);
        _writerGate = writerGate;
        _files = new LogFileWriter(directory, limits, clock);
        _gate = new ColdStartGate(minimum, Enqueue);

        _writer = new Thread(Run) { IsBackground = true, Name = "CorePin.FileLog" };
        _writer.Start();
    }

    public static FileLog Create(string logDirectory, LogLevel minimum, IClock clock)
        => Create(logDirectory, minimum, clock, FileLogLimits.Default, writerGate: null);

    /// Test seam: production limits are unreachable in a test; writerGate holds the writer.
    internal static FileLog CreateForTests(string logDirectory, LogLevel minimum, IClock clock,
                                           FileLogLimits limits, ManualResetEventSlim? writerGate = null)
        => Create(logDirectory, minimum, clock, limits, writerGate);

    private static FileLog Create(string logDirectory, LogLevel minimum, IClock clock,
                                  FileLogLimits limits, ManualResetEventSlim? writerGate)
    {
        string resolved = LogFileWriter.ResolveDirectory(logDirectory, out bool usedFallback);
        var log = new FileLog(resolved, minimum, clock, limits, writerGate);
        if (usedFallback)
            log.Warning("app", "primary log location unavailable, using fallback location");
        return log;
    }

    /// The directory ACTUALLY in use — the fallback path, or EMPTY if nothing could be made.
    public string Directory => _files.Directory;

    public LogLevel Minimum
    {
        get => _gate.Minimum;
        set => _gate.Minimum = value;
    }

    public bool IsEnabled(LogLevel level) => _gate.IsEnabled(level);

    public void Write(LogLevel level, string category, string message)
        => Submit(level, category, message, forced: false);

    /// Bypasses Minimum; only app.start, topology.summary, app.log-level-override, app.exit.
    public void WriteAlways(LogLevel level, string category, string message)
        => Submit(level, category, message, forced: true);

    public void Flush()
    {
        if (_closed) return;
        using var signal = new ManualResetEventSlim(false);
        if (TryEnqueue(new QueueItem(QueueItemKind.SyncMarker, Signal: signal)))
            signal.Wait(FlushTimeoutMs);
    }

    public void Dispose()
    {
        _gate.ReleaseBuffer();

        _closed = true;
        // A second Dispose finds the queue disposed; there is nothing left to complete.
        try { _queue.CompleteAdding(); }
        catch (ObjectDisposedException) { }

        bool joined = _writer.Join(JoinTimeoutMs);

        // Released only when the writer is provably done, or it hits ObjectDisposedException.
        if (joined)
        {
            _files.Dispose();
            _queue.Dispose();
        }
    }

    private void Submit(LogLevel level, string category, string message, bool forced)
        => _gate.Submit(new LogEntry(_clock.UtcNow, level, category, message, forced));

    private void Enqueue(LogEntry entry)
    {
        if (!TryEnqueue(new QueueItem(QueueItemKind.Line, entry))) Interlocked.Increment(ref _dropped);
    }

    internal bool TryEnqueue(QueueItem item)
    {
        if (_closed) return false;
        try { return _queue.TryAdd(item); }
        catch (InvalidOperationException) { return false; }
    }

    private void Run()
    {
        // Two nets: a Handle failure costs one line, a loop failure would kill the process.
        try
        {
            _writerGate?.Wait();
            _files.Open();

            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try { Handle(item); }
                catch (Exception ex) { TraceOnly(ex); }

                if (_queue.Count == 0) EndOfBatch();
            }
            EndOfBatch();
        }
        catch (Exception ex) { TraceOnly(ex); }
    }

    private void Handle(QueueItem item)
    {
        switch (item.Kind)
        {
            case QueueItemKind.Line:
                if (item.Line is { } entry) _files.Write(FormatLine(entry));
                break;
            case QueueItemKind.SyncMarker:
                _files.Flush();
                item.Signal?.Set();
                break;
        }
    }

    private void EndOfBatch()
    {
        int dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
        {
            _files.Write(FormatLine(new LogEntry(_clock.UtcNow, LogLevel.Warning, "app",
                $"log queue overflow, {dropped} lines dropped since last report")));
        }
        _files.Flush();
    }

    internal static string FormatLine(LogEntry entry)
    {
        string timestamp = entry.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        string level = entry.Level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT ",
            _ => "?????",
        };
        return $"{timestamp} {level} {entry.Category.PadRight(8)} {Escape(entry.Message)}";
    }

    private static string Escape(string message)
        => message.Replace("\r\n", "\\n", StringComparison.Ordinal)
                  .Replace("\n", "\\n", StringComparison.Ordinal)
                  .Replace("\r", "\\n", StringComparison.Ordinal);

    internal static void TraceOnly(Exception ex) => Trace.WriteLine("CorePin.FileLog: " + ex);
}
