using CorePin.Core.Time;

namespace CorePin.Interop;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public long MonotonicMs => Environment.TickCount64;
}
