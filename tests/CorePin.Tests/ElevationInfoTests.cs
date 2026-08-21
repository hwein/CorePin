using CorePin.Interop;

namespace CorePin.Tests;

public static class ElevationInfoTests
{
    /// Both values depend on how the test run was started; only that they are readable is checked.
    public static void Test_ElevationInfo_DoesNotThrow()
    {
        _ = ElevationInfo.IsElevated;
        _ = ElevationInfo.CanElevate;
    }

    /// CanElevate reads the limited half of a split token, and that half is never the elevated one.
    public static void Test_CanElevate_IsNeverTrueTogetherWithIsElevated()
    {
        Assert.True(!(ElevationInfo.CanElevate && ElevationInfo.IsElevated),
            "a limited token is by definition not an elevated token");
    }
}
