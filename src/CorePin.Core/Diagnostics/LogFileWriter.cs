using System.Diagnostics;
using System.Globalization;
using System.Text;
using CorePin.Core.Time;

namespace CorePin.Core.Diagnostics;

/// Owned by the FileLog writer thread alone: file naming, rolling and the three size limits.
internal sealed class LogFileWriter : IDisposable
{
    private const string FilePrefix = "CorePin_";
    private const string FileExtension = ".log";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IClock _clock;
    private readonly FileLogLimits _limits;
    private readonly string _sessionStamp;
    private readonly List<string> _sessionFiles = new();

    private FileStream? _stream;
    private string _currentFile = string.Empty;
    private long _bytesInFile;
    private int _filePart;                                // 1 = origin, 2.. = continuation
    private bool _disabled;
    private bool _pendingSessionLimitNote;

    public LogFileWriter(string directory, FileLogLimits limits, IClock clock)
    {
        Directory = directory;
        _limits = limits;
        _clock = clock;
        _sessionStamp = clock.UtcNow.ToString("yyyyMMdd_HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    /// The directory ACTUALLY in use — the fallback path, or EMPTY if nothing could be made.
    public string Directory { get; }

    public void Open() => OpenNextFile();

    public void Write(string line)
    {
#if DEBUG
        Trace.WriteLine(line);
#endif
        if (_disabled || _stream is null) return;

        byte[] bytes = Utf8NoBom.GetBytes(line + "\n");
        if (_bytesInFile > 0 && _bytesInFile + bytes.Length > _limits.FileSizeBytes) RollToContinuation();
        if (_disabled || _stream is null) return;

        try
        {
            _stream.Write(bytes, 0, bytes.Length);
            _bytesInFile += bytes.Length;
        }
        catch (Exception ex)
        {
            // Disk full or handle lost: stop writing for the rest of the session.
            FileLog.TraceOnly(ex);
            _disabled = true;
        }
    }

    public void Flush()
    {
        try { _stream?.Flush(); }
        catch (Exception ex) { FileLog.TraceOnly(ex); _disabled = true; }
    }

    public void Dispose()
    {
        try { _stream?.Dispose(); }
        catch (Exception ex) { FileLog.TraceOnly(ex); }
        _stream = null;
    }

    public static string ResolveDirectory(string primary, out bool usedFallback)
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
        catch (Exception ex) { FileLog.TraceOnly(ex); return false; }
    }

    private void RollToContinuation()
    {
        if (_sessionFiles.Count >= _limits.SessionFiles) DropOldestContinuation();

        string previous = Path.GetFileName(_currentFile);
        try { _stream?.Dispose(); }
        catch (Exception ex) { FileLog.TraceOnly(ex); }
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

        try { File.Delete(oldest); } catch (Exception ex) { FileLog.TraceOnly(ex); }
        _sessionFiles.Remove(oldest);
        _pendingSessionLimitNote = true;
    }

    private void WriteHeader(string message)
    {
        var header = new LogEntry(_clock.UtcNow, LogLevel.Warn, "app", message);
        byte[] bytes = Utf8NoBom.GetBytes(FileLog.FormatLine(header) + "\n");
        try { _stream?.Write(bytes, 0, bytes.Length); _bytesInFile += bytes.Length; }
        catch (Exception ex) { FileLog.TraceOnly(ex); _disabled = true; }

        if (_pendingSessionLimitNote)
        {
            _pendingSessionLimitNote = false;
            // From the limit in force, so the line cannot claim a number that is not enforced.
            Write(FileLog.FormatLine(new LogEntry(_clock.UtcNow, LogLevel.Warn, "app",
                $"session log size limit reached ({_limits.SessionFiles.ToString(CultureInfo.InvariantCulture)} files), "
                + "oldest continuation file removed")));
        }
    }

    private void OpenNextFile()
    {
        if (Directory.Length == 0) { _disabled = true; return; }

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
            catch (IOException ex) { FileLog.TraceOnly(ex); }
            catch (UnauthorizedAccessException ex) { FileLog.TraceOnly(ex); _disabled = true; return; }
        }

        _disabled = true;
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
        catch (Exception ex) { FileLog.TraceOnly(ex); return; }

        long total = foreign.Sum(f => f.Length);
        foreach (var file in foreign)
        {
            if (total <= _limits.DirectoryBudgetBytes) return;
            try { file.Delete(); total -= file.Length; }
            catch (Exception ex) { FileLog.TraceOnly(ex); }
        }
    }
}
