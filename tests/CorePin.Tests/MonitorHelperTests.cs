using CorePin.Interop;

namespace CorePin.Tests;

/// Monitor geometry has no fixture: these ask the running machine, unlike the other
/// CorePin.Interop tests, which walk recorded buffers.
public static class MonitorHelperTests
{
    public static void Test_IntersectsAnyMonitor_FarAwayRectIsFalse()
    {
        Assert.True(!MonitorHelper.IntersectsAnyMonitor(new DipRect(-30000, -30000, 100, 100)),
            "a rectangle far outside every desktop coordinate hits no monitor");
    }

    public static void Test_IntersectsAnyMonitor_PrimaryOriginIsTrue()
    {
        Assert.True(MonitorHelper.IntersectsAnyMonitor(new DipRect(0, 0, 100, 100)),
            "the point (0,0) always lies on the primary monitor, so a rectangle there hits it");
    }

    public static void Test_WorkAreaAtCursor_ReturnsPositiveArea()
    {
        var area = MonitorHelper.WorkAreaAtCursor();

        Assert.True(area.Width > 0 && area.Height > 0,
            $"the work area of a real monitor has an extent, got {area.Width}x{area.Height}");
    }
}
