using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CorePin.Core.Time;

namespace CorePin.Core.Diagnostics;

internal readonly record struct LogEntry(
    DateTime TimestampUtc, LogLevel Level, string Category, string Message, bool Forced = false);

internal enum QueueItemKind { Line, SyncMarker }

internal readonly record struct QueueItem(
    QueueItemKind Kind, LogEntry? Line = null, ManualResetEventSlim? Signal = null);

/// Only the test seam (CreateForTests) ever passes anything but Default.
internal readonly record struct FileLogLimits(
    long FileSizeBytes, int SessionFiles, long DirectoryBudgetBytes, int QueueCapacity)
{
    public static readonly FileLogLimits Default =
        new(10L * 1024 * 1024, 5, 50L * 1024 * 1024, 2000);
}

/// One background writer, one queue. Write never blocks and never throws.
public sealed class FileLog : ILog, IDisposable
{
    private const int ColdStartCapacity = 200;
    private const int JoinTimeoutMs = 1000;
    private const int FlushTimeoutMs = 250;
    private const string FilePrefix = "CorePin_";
    private const string FileExtension = ".log";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IClock _clock;
    private readonly FileLogLimits _limits;
    private readonly BlockingCollection<QueueItem> _queue;
    private readonly ManualResetEventSlim? _writerGate;
    private readonly Thread _writer;
    private readonly string _sessionStamp;

    // Cold start: lines written before Minimum is assigned are buffered and replayed later.
    private readonly object _coldStartGate = new();
    private List<LogEntry>? _coldStart = new(ColdStartCapacity);

    private volatile LogLevel _minimum;
    private volatile bool _closed;
    private int _dropped;

    // Fields below are touched by the writer thread only.
    private FileStream? _stream;
    private string _currentFile = string.Empty;
    private long _bytesInFile;
    private int _filePart;                                // 1 = origin, 2.. = continuation
    private readonly List<string> _sessionFiles = new();
    private bool _writeDisabled;
    private bool _pendingSessionLimitNote;

    private FileLog(string directory, LogLevel minimum, IClock clock,
                    FileLogLimits limits, ManualResetEventSlim? writerGate)
    {
        _clock = clock;
        _limits = limits;
        _minimum = minimum;
        _queue = new BlockingCollection<QueueItem>(limits.QueueCapacity);
        _writerGate = writerGate;
        Directory = directory;
        _sessionStamp = clock.UtcNow.ToString("yyyyMMdd_HHmmss'Z'", CultureInfo.InvariantCulture);

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
        string resolved = ResolveDirectory(logDirectory, out bool usedFallback);
        var log = new FileLog(resolved, minimum, clock, limits, writerGate);
        if (usedFallback)
            log.Warn("app", "primary log location unavailable, using fallback location");
        return log;
    }

    /// The directory ACTUALLY in use — the fallback path, or EMPTY if nothing could be made.
    public string Directory { get; }

    public LogLevel Minimum
    {
        get => _minimum;
        set
        {
            // Inside the lock so a concurrent Write cannot reach the queue before the replay.
            lock (_coldStartGate)
            {
                _minimum = value;
                var pending = _coldStart;
                _coldStart = null;
                if (pending is not null)
                    foreach (var entry in pending) Emit(entry);
            }
        }
    }

    /// Open until Minimum is assigned, so an IsEnabled-guarded Write still reaches the buffer.
    public bool IsEnabled(LogLevel level) => Volatile.Read(ref _coldStart) is not null || level >= _minimum;

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
        // A never-assigned Minimum would swallow the whole session.
        if (Volatile.Read(ref _coldStart) is not null) Minimum = _minimum;

        _closed = true;
        // A second Dispose finds the queue disposed; there is nothing left to complete.
        try { _queue.CompleteAdding(); }
        catch (ObjectDisposedException) { }

        bool joined = _writer.Join(JoinTimeoutMs);

        // Released only when the writer is provably done, or it hits ObjectDisposedException.
        if (joined)
        {
            try { _stream?.Dispose(); }
            catch (Exception ex) { TraceOnly(ex); }
            _stream = null;
            _queue.Dispose();
        }
    }

    private void Submit(LogLevel level, string category, string message, bool forced)
    {
        var entry = new LogEntry(_clock.UtcNow, level, category, message, forced);

        // Double-checked: after the first Minimum assignment no caller takes the lock again.
        if (Volatile.Read(ref _coldStart) is null) { Emit(entry); return; }

        lock (_coldStartGate)
        {
            if (_coldStart is { } buffer)
            {
                // Over the limit the YOUNGEST lines are dropped; a session start must survive.
                if (buffer.Count < ColdStartCapacity) buffer.Add(entry);
                return;
            }

            Emit(entry);
        }
    }

    private void Emit(LogEntry entry)
    {
        if (!entry.Forced && entry.Level < _minimum) return;
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
            OpenNextFile();

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
                if (item.Line is { } entry) WriteToFile(FormatLine(entry));
                break;
            case QueueItemKind.SyncMarker:
                FlushStream();
                item.Signal?.Set();
                break;
        }
    }

    private void EndOfBatch()
    {
        int dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
        {
            WriteToFile(FormatLine(new LogEntry(_clock.UtcNow, LogLevel.Warn, "app",
                $"log queue overflow, {dropped} lines dropped since last report")));
        }
        FlushStream();
    }

    private void FlushStream()
    {
        try { _stream?.Flush(); }
        catch (Exception ex) { TraceOnly(ex); _writeDisabled = true; }
    }

    private void WriteToFile(string line)
    {
#if DEBUG
        Trace.WriteLine(line);
#endif
        if (_writeDisabled || _stream is null) return;

        byte[] bytes = Utf8NoBom.GetBytes(line + "\n");
        if (_bytesInFile > 0 && _bytesInFile + bytes.Length > _limits.FileSizeBytes) RollToContinuation();
        if (_writeDisabled || _stream is null) return;

        try
        {
            _stream.Write(bytes, 0, bytes.Length);
            _bytesInFile += bytes.Length;
        }
        catch (Exception ex)
        {
            // Disk full or handle lost: stop writing for the rest of the session.
            TraceOnly(ex);
            _writeDisabled = true;
        }
    }

    private void RollToContinuation()
    {
        if (_sessionFiles.Count >= _limits.SessionFiles) DropOldestContinuation();

        string previous = Path.GetFileName(_currentFile);
        try { _stream?.Dispose(); }
        catch (Exception ex) { TraceOnly(ex); }
        _stream = null;

        OpenNextFile();
        if (_stream is null) return;

        long megabytes = _limits.FileSizeBytes / (1024 * 1024);
        // File name only, never the path.
        WriteHeader($"log file size limit reached ({megabytes.ToString(CultureInfo.InvariantCulture)} MB), "
                    + $"continued from {previous}");
    }

    private void DropOldestContinuation()
    {
        // Never the origin file — it carries the session header.
        string? oldest = _sessionFiles.Skip(1).FirstOrDefault();
        if (oldest is null) return;

        try { File.Delete(oldest); } catch (Exception ex) { TraceOnly(ex); }
        _sessionFiles.Remove(oldest);
        _pendingSessionLimitNote = true;
    }

    private void WriteHeader(string message)
    {
        var header = new LogEntry(_clock.UtcNow, LogLevel.Warn, "app", message);
        byte[] bytes = Utf8NoBom.GetBytes(FormatLine(header) + "\n");
        try { _stream?.Write(bytes, 0, bytes.Length); _bytesInFile += bytes.Length; }
        catch (Exception ex) { TraceOnly(ex); _writeDisabled = true; }

        if (_pendingSessionLimitNote)
        {
            _pendingSessionLimitNote = false;
            // From the limit in force, so the line cannot claim a number that is not enforced.
            WriteToFile(FormatLine(new LogEntry(_clock.UtcNow, LogLevel.Warn, "app",
                $"session log size limit reached ({_limits.SessionFiles.ToString(CultureInfo.InvariantCulture)} files), "
                + "oldest continuation file removed")));
        }
    }

    private void OpenNextFile()
    {
        if (Directory.Length == 0) { _writeDisabled = true; return; }

        // At session start and at every new file, on the writer thread.
        EnforceDirectoryBudget();

        _filePart++;
        string baseName = _filePart == 1
            ? FilePrefix + _sessionStamp
            : FilePrefix + _sessionStamp + "_part" + _filePart.ToString(CultureInfo.InvariantCulture);

        for (int attempt = 1; attempt <= 50; attempt++)
        {
            string suffix = attempt == 1 ? string.Empty : "-" + attempt.ToString(CultureInfo.InvariantCulture);
            string path = Path.Combine(Directory, baseName + suffix + FileExtension);
            try
            {
                _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                _currentFile = path;
                _bytesInFile = 0;
                _sessionFiles.Add(path);
                return;
            }
            catch (IOException ex) { TraceOnly(ex); }
            catch (UnauthorizedAccessException ex) { TraceOnly(ex); _writeDisabled = true; return; }
        }

        _writeDisabled = true;
    }

    /// Only FOREIGN, older session prefixes count — a session never loses its own head file.
    private void EnforceDirectoryBudget()
    {
        if (Directory.Length == 0) return;

        List<FileInfo> foreign;
        try
        {
            foreign = new DirectoryInfo(Directory)
                .EnumerateFiles(FilePrefix + "*" + FileExtension)
                .Where(f => !f.Name.StartsWith(FilePrefix + _sessionStamp, StringComparison.Ordinal))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex) { TraceOnly(ex); return; }

        long total = foreign.Sum(f => f.Length);
        foreach (var file in foreign)
        {
            if (total <= _limits.DirectoryBudgetBytes) return;
            try { file.Delete(); total -= file.Length; }
            catch (Exception ex) { TraceOnly(ex); }
        }
    }

    internal static string FormatLine(LogEntry entry)
    {
        string timestamp = entry.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        string level = entry.Level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warn => "WARN ",
            _ => "?????",
        };
        return $"{timestamp} {level} {entry.Category.PadRight(8)} {Escape(entry.Message)}";
    }

    private static string Escape(string message)
        => message.Replace("\r\n", "\\n", StringComparison.Ordinal)
                  .Replace("\n", "\\n", StringComparison.Ordinal)
                  .Replace("\r", "\\n", StringComparison.Ordinal);

    private static string ResolveDirectory(string primary, out bool usedFallback)
    {
        usedFallback = false;
        if (TryCreateDirectory(primary)) return primary;

        string fallback = Path.Combine(Path.GetTempPath(), "CorePin", "logs");
        if (TryCreateDirectory(fallback)) { usedFallback = true; return fallback; }

        return string.Empty;
    }

    private static bool TryCreateDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory)) return false;
        try { System.IO.Directory.CreateDirectory(directory); return true; }
        catch (Exception ex) { TraceOnly(ex); return false; }
    }

    private static void TraceOnly(Exception ex) => Trace.WriteLine("CorePin.FileLog: " + ex);
}
