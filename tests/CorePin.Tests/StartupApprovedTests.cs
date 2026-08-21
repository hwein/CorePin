using CorePin.Core.Autostart;

namespace CorePin.Tests;

public static class StartupApprovedTests
{
    public static void Test_IsDisabled_NullIsEnabled()
    {
        Assert.True(!StartupApproved.IsDisabled(null), "a missing value means Windows never turned the entry off");
    }

    public static void Test_IsDisabled_EmptyIsEnabled()
    {
        Assert.True(!StartupApproved.IsDisabled([]), "a zero-length value has no byte 0 to read");
    }

    public static void Test_IsDisabled_0x02PlusZeroFiletimeIsEnabled()
    {
        Assert.True(!StartupApproved.IsDisabled([0x02, 0, 0, 0, 0, 0, 0, 0, 0]), "byte 0 = 0x02 leaves bit 0 clear");
    }

    public static void Test_IsDisabled_0x03PlusFiletimeIsDisabled()
    {
        Assert.True(StartupApproved.IsDisabled([0x03, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88]), "byte 0 = 0x03 sets bit 0");
    }

    public static void Test_IsDisabled_0x06IsEnabled()
    {
        Assert.True(!StartupApproved.IsDisabled([0x06, 0, 0, 0, 0, 0, 0, 0, 0]), "byte 0 = 0x06 leaves bit 0 clear");
    }

    public static void Test_IsDisabled_0x07IsDisabled()
    {
        Assert.True(StartupApproved.IsDisabled([0x07, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88]), "byte 0 = 0x07 sets bit 0");
    }

    public static void Test_IsDisabled_SingleByte0x03IsDisabled()
    {
        Assert.True(StartupApproved.IsDisabled([0x03]), "the minimum readable length is 1 byte");
    }
}
