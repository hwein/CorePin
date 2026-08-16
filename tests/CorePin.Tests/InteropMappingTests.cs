using CorePin.Core.Platform;
using CorePin.Core.Primitives;
using CorePin.Interop;

namespace CorePin.Tests;

/// The two pure mapping functions of the interop layer. Call-free by design: a test in
/// this project must not invoke Win32, so neither of them may be inlined at its call site.
public static class InteropMappingTests
{
    public static void Test_MaskConversion_RoundTrips_Zero()
    {
        var mask = new AffinityMask(0);
        Assert.Equal(mask, MaskConversion.ToCore(MaskConversion.ToNative(mask)),
            "0 round-trips through ToNative/ToCore");
    }

    public static void Test_MaskConversion_RoundTrips_AllBits()
    {
        var mask = new AffinityMask(ulong.MaxValue);
        Assert.Equal(mask, MaskConversion.ToCore(MaskConversion.ToNative(mask)),
            "ulong.MaxValue round-trips through ToNative/ToCore");
    }

    public static void Test_MaskConversion_RoundTrips_Bit0()
    {
        var mask = new AffinityMask(1UL << 0);
        Assert.Equal(mask, MaskConversion.ToCore(MaskConversion.ToNative(mask)),
            "bit 0 alone round-trips through ToNative/ToCore");
    }

    public static void Test_MaskConversion_RoundTrips_Bit63()
    {
        var mask = new AffinityMask(1UL << 63);
        Assert.Equal(mask, MaskConversion.ToCore(MaskConversion.ToNative(mask)),
            "bit 63 alone round-trips through ToNative/ToCore");
    }

    public static void Test_ClassifyOpenFailure_AccessDenied()
    {
        Assert.Equal(OpenFailure.AccessDenied, Win32ErrorMapping.ClassifyOpenFailure(5),
            "error 5 (ACCESS_DENIED) maps to AccessDenied");
    }

    public static void Test_ClassifyOpenFailure_Gone()
    {
        Assert.Equal(OpenFailure.Gone, Win32ErrorMapping.ClassifyOpenFailure(87),
            "error 87 (INVALID_PARAMETER) maps to Gone");
    }

    public static void Test_ClassifyOpenFailure_Other_InsufficientBuffer()
    {
        Assert.Equal(OpenFailure.Other, Win32ErrorMapping.ClassifyOpenFailure(122),
            "an unmapped error code (122) maps to Other");
    }

    public static void Test_ClassifyOpenFailure_Other_InvalidHandle()
    {
        Assert.Equal(OpenFailure.Other, Win32ErrorMapping.ClassifyOpenFailure(6),
            "an unmapped error code (6) maps to Other");
    }
}
