namespace CorePin.Core.Diagnostics;

/// Lines written before Minimum is assigned are buffered here and replayed on the assignment.
internal sealed class ColdStartGate(LogLevel minimum, Action<LogEntry> emit)
{
    private const int Capacity = 200;

    private readonly object _lock = new();

    private List<LogEntry>? _buffered = new(Capacity);
    private volatile LogLevel _minimum = minimum;

    public LogLevel Minimum
    {
        get => _minimum;
        set
        {
            // Inside the lock so a concurrent Submit cannot reach the queue before the replay.
            lock (_lock)
            {
                _minimum = value;
                Release();
            }
        }
    }

    /// Open until Minimum is assigned, so an IsEnabled-guarded Write still reaches the buffer.
    public bool IsEnabled(LogLevel level) => Volatile.Read(ref _buffered) is not null || level >= _minimum;

    public void Submit(LogEntry entry)
    {
        // Double-checked: after the first Minimum assignment no caller takes the lock again.
        if (Volatile.Read(ref _buffered) is null) { Pass(entry); return; }

        lock (_lock)
        {
            if (_buffered is { } buffer)
            {
                // Over the limit the YOUNGEST lines are dropped; a session start must survive.
                if (buffer.Count < Capacity) buffer.Add(entry);
                return;
            }

            Pass(entry);
        }
    }

    /// A never-assigned Minimum would swallow the whole session.
    public void ReleaseBuffer()
    {
        lock (_lock) Release();
    }

    private void Release()
    {
        var pending = _buffered;
        _buffered = null;
        if (pending is null) return;

        foreach (var entry in pending) Pass(entry);
    }

    private void Pass(LogEntry entry)
    {
        if (!entry.Forced && entry.Level < _minimum) return;
        emit(entry);
    }
}
