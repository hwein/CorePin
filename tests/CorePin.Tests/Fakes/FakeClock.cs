using CorePin.Core.Time;

namespace CorePin.Tests.Fakes;

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 8, 15, 9, 12, 3, 1, DateTimeKind.Utc);

    public long MonotonicMs { get; set; }

    public void Advance(int ms)
    {
        UtcNow = UtcNow.AddMilliseconds(ms);
        MonotonicMs += ms;
    }
}
