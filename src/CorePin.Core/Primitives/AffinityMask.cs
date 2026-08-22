using System.Globalization;
using System.Numerics;

namespace CorePin.Core.Primitives;      // NOT Rules — masks are a primitive, not a rule concept

/// A 64-bit processor affinity mask. Bit i is logical processor i in group 0.
public readonly struct AffinityMask : IEquatable<AffinityMask>
{
    /// The target platform is capped at 64 logical processors in one processor group.
    private const int MaxThread = 63;

    public AffinityMask(ulong value) => Value = value;

    public ulong Value { get; }

    public static AffinityMask Empty => default;

    public bool IsEmpty => Value == 0;

    public int Count => BitOperations.PopCount(Value);

    public bool Contains(int thread)
        => (uint)thread <= MaxThread && (Value & (1UL << thread)) != 0;

    public AffinityMask With(int thread)
    {
        Require(thread);
        return new AffinityMask(Value | (1UL << thread));
    }

    public AffinityMask Without(int thread)
    {
        Require(thread);
        return new AffinityMask(Value & ~(1UL << thread));
    }

    public bool FitsInto(AffinityMask other) => (Value & other.Value) == Value;

    public static AffinityMask FromThreads(IEnumerable<int> threads)
    {
        ArgumentNullException.ThrowIfNull(threads);

        ulong value = 0;
        foreach (int thread in threads)
        {
            Require(thread);
            value |= 1UL << thread;
        }
        return new AffinityMask(value);
    }

    public IReadOnlyList<int> ToThreads()
    {
        var result = new List<int>(Count);
        ulong rest = Value;
        while (rest != 0)
        {
            int bit = BitOperations.TrailingZeroCount(rest);
            result.Add(bit);
            rest &= rest - 1;
        }
        return result;
    }

    /// "0x" plus exactly 16 uppercase hex digits — the canonical form of the dump.
    public static string ToHex(ulong value)
        => "0x" + value.ToString("X16", CultureInfo.InvariantCulture);

    public string ToHex() => ToHex(Value);

    public bool Equals(AffinityMask other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is AffinityMask other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => ToHex();

    public static bool operator ==(AffinityMask left, AffinityMask right) => left.Equals(right);

    public static bool operator !=(AffinityMask left, AffinityMask right) => !left.Equals(right);

    private static void Require(int thread)
    {
        if ((uint)thread > MaxThread)
            throw new ArgumentOutOfRangeException(nameof(thread), thread,
                "thread index must be between 0 and 63");
    }
}
