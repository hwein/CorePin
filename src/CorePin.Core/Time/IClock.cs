namespace CorePin.Core.Time;

public interface IClock
{
    DateTime UtcNow { get; }
    long MonotonicMs { get; }
}
