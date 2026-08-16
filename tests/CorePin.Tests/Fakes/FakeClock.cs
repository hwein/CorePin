using CorePin.Core.Time;

namespace CorePin.Tests.Fakes;

/// Guarded, because the engine smoke tests read it from the worker thread while the test
/// advances it.
public sealed class FakeClock : IClock
{
    private readonly Lock _gate = new();
    private DateTime _utcNow = new(2026, 8, 15, 9, 12, 3, 1, DateTimeKind.Utc);
    private long _monotonicMs;

    public DateTime UtcNow
    {
        get { lock (_gate) return _utcNow; }
        set { lock (_gate) _utcNow = value; }
    }

    public long MonotonicMs
    {
        get { lock (_gate) return _monotonicMs; }
        set { lock (_gate) _monotonicMs = value; }
    }

    public void Advance(int ms)
    {
        lock (_gate)
        {
            _utcNow = _utcNow.AddMilliseconds(ms);
            _monotonicMs += ms;
        }
    }
}
