using CorePin.Interop;

namespace CorePin.Tests;

public static class ElevationInfoTests
{
    /// The value depends on how the test run was started; only that it is readable is checked.
    public static void Test_ElevationInfo_DoesNotThrow()
    {
        _ = ElevationInfo.IsElevated;
    }
}
