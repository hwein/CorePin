using CorePin.Core.Primitives;

namespace CorePin.Core.Platform;

public readonly record struct AffinityPair(AffinityMask Process, AffinityMask System);
