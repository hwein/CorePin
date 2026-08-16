using CorePin.Core.Primitives;

namespace CorePin.Interop;

/// Named, not inline: the test runner needs a member it can call directly.
internal static class MaskConversion
{
    internal static nuint ToNative(AffinityMask mask) => (nuint)mask.Value;

    internal static AffinityMask ToCore(nuint native) => new((ulong)native);
}
