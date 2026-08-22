using System.Buffers.Binary;
using CorePin.Interop;
using CorePin.Tests.Fakes;

namespace CorePin.Tests;

/// The pure pieces of the tray/notification-icon interop layer — translation and
/// formatting functions that need no real window, tray icon, or menu to run.
public static class TrayInteropTests
{
    public static void Test_ExtractTrayEvent_Zero()
    {
        Assert.Equal(0u, MessageWindow.ExtractTrayEvent(0), "a zero lParam yields event code 0");
    }

    public static void Test_ExtractTrayEvent_AllLowBitsSet()
    {
        Assert.Equal(0xFFFFu, MessageWindow.ExtractTrayEvent((nint)0xFFFF),
            "LOWORD 0xFFFF passes through unchanged");
    }

    public static void Test_ExtractTrayEvent_MixedHighAndLowBits()
    {
        Assert.Equal(0x0400u, MessageWindow.ExtractTrayEvent((nint)0x0001_0400),
            "only the low word is read; the high word (0x0001) is discarded");
    }

    public static void Test_ExtractTrayEvent_NegativeNintUsesLowWordOnly()
    {
        long raw = long.MinValue | 0x8001L;   // sign bit set (negative) plus low word 0x8001
        Assert.Equal(0x8001u, MessageWindow.ExtractTrayEvent((nint)raw),
            "a negative nint (sign bit set) still yields the plain low word");
    }

    public static void Test_ExtractTrayEvent_HighNintIgnoresUpperBits()
    {
        long raw = 0x00007FF6_00000000L | 0x0305L;   // a plausible x64 address, low word 0x0305
        Assert.Equal(0x0305u, MessageWindow.ExtractTrayEvent((nint)raw),
            "a large 64-bit-range value still reduces to its low word");
    }

    public static void Test_Classify_ContextMenuIsRight()
    {
        Assert.Equal(TrayIcon.TrayClick.Right, TrayIcon.Classify(0x007B),
            "WM_CONTEXTMENU (0x007B) is a right click");
    }

    public static void Test_Classify_SelectIsLeft()
    {
        Assert.Equal(TrayIcon.TrayClick.Left, TrayIcon.Classify(0x0400),
            "NIN_SELECT (0x0400) is a left click");
    }

    public static void Test_Classify_KeySelectIsLeft()
    {
        Assert.Equal(TrayIcon.TrayClick.Left, TrayIcon.Classify(0x0401),
            "NIN_KEYSELECT (0x0401) is a left click");
    }

    public static void Test_Classify_UnknownEventIsNone()
    {
        Assert.Equal(TrayIcon.TrayClick.None, TrayIcon.Classify(0x0200),
            "an unrelated notify code classifies as None");
    }

    public static void Test_Format_ZeroRulesZeroActive()
    {
        Assert.Equal("CorePin — 0 rules, 0 active", TrayTooltip.Format(0, 0), "zero rules keeps the plural");
    }

    public static void Test_Format_OneRuleDropsPlural()
    {
        Assert.Equal("CorePin — 1 rule, 0 active", TrayTooltip.Format(1, 0), "exactly one rule drops the trailing s");
    }

    public static void Test_Format_MultipleRulesSomeActive()
    {
        Assert.Equal("CorePin — 3 rules, 1 active", TrayTooltip.Format(3, 1), "several rules, some of them applied");
    }

    public static void Test_FromCommand_MapsDefinedIds()
    {
        Assert.Equal(TrayMenuItem.OpenCorePin, TrayMenu.FromCommand(1), "id 1 is OpenCorePin");
        Assert.Equal(TrayMenuItem.CopyTopology, TrayMenu.FromCommand(2), "id 2 is CopyTopology");
        Assert.Equal(TrayMenuItem.OpenLogFolder, TrayMenu.FromCommand(3), "id 3 is OpenLogFolder");
        Assert.Equal(TrayMenuItem.Exit, TrayMenu.FromCommand(4), "id 4 is Exit");
        Assert.Equal(TrayMenuItem.Autostart, TrayMenu.FromCommand(5), "id 5 is Autostart");
    }

    public static void Test_FromCommand_ZeroIsNone()
    {
        Assert.Equal(TrayMenuItem.None, TrayMenu.FromCommand(0),
            "0 is the TrackPopupMenu cancel return and maps to None");
    }

    public static void Test_FromCommand_OutOfRangeIdsAreNone()
    {
        Assert.Equal(TrayMenuItem.None, TrayMenu.FromCommand(-1), "a negative id maps to None");
        Assert.Equal(TrayMenuItem.None, TrayMenu.FromCommand(6), "an id past the last defined item maps to None");
        Assert.Equal(TrayMenuItem.None, TrayMenu.FromCommand(7), "an id past the last defined item maps to None");
        Assert.Equal(TrayMenuItem.None, TrayMenu.FromCommand(8), "an id past the last defined item maps to None");
    }

    public static void Test_SelectFrameIndex_ExactMatchIsChosen()
    {
        byte[] ico = IconDirectory((16, 0), (32, 0), (48, 0));
        Assert.Equal(1, IconResources.SelectFrameIndex(ico, 32), "the exact 32px frame is index 1");
    }

    public static void Test_SelectFrameIndex_BetweenTwoSizesPicksLargerWithoutExceeding()
    {
        byte[] ico = IconDirectory((16, 0), (48, 0));
        Assert.Equal(0, IconResources.SelectFrameIndex(ico, 32),
            "16px is the largest frame that does not exceed the wanted 32px");
    }

    public static void Test_SelectFrameIndex_AllFramesLargerThanDesiredPicksSmallest()
    {
        byte[] ico = IconDirectory((48, 0), (32, 0), (64, 0));
        Assert.Equal(1, IconResources.SelectFrameIndex(ico, 16),
            "no frame fits within 16px, so the smallest of them (32px, index 1) wins");
    }

    public static void Test_SelectFrameIndex_ZeroByteMeans256()
    {
        byte[] ico = IconDirectory((0, 0), (48, 0));
        Assert.Equal(0, IconResources.SelectFrameIndex(ico, 256),
            "bWidth 0 is compared as 256, an exact match against the 256px request");
    }

    public static void Test_SelectFrameIndex_WrongIdTypeIsRejected()
    {
        byte[] ico = IconDirectory(idType: 2, (32, 0));
        Assert.Equal(-1, IconResources.SelectFrameIndex(ico, 32), "idType != 1 is not an icon directory");
    }

    public static void Test_SelectFrameIndex_ZeroCountIsRejected()
    {
        byte[] ico = IconDirectory();
        Assert.Equal(-1, IconResources.SelectFrameIndex(ico, 32), "idCount 0 leaves no frame to choose");
    }

    public static void Test_SelectFrameIndex_TruncatedDirectoryIsRejected()
    {
        byte[] full = IconDirectory((16, 0), (32, 0));
        byte[] truncated = full[..^1];
        Assert.Equal(-1, IconResources.SelectFrameIndex(truncated, 32),
            "a directory cut short of its declared entry count is rejected");
    }

    public static void Test_OpenLogFolder_EmptyPathDoesNothing()
    {
        var log = new RecordingLog();

        Assert.True(!LogFolder.TryOpen("", log), "an empty path is refused");
        Assert.True(!LogFolder.TryOpen(null, log), "a null path is refused");
        Assert.Equal(0, log.Lines.Count, "the empty-path guard returns before it could log anything");
    }

    /// A minimal ICONDIR + ICONDIRENTRY stream: 6-byte header, then one 16-byte entry per
    /// (width, height) frame — SelectFrameIndex reads only bWidth/bHeight at entry offset 0/1.
    private static byte[] IconDirectory(params (byte Width, byte Height)[] frames) => IconDirectory(1, frames);

    private static byte[] IconDirectory(ushort idType, params (byte Width, byte Height)[] frames)
    {
        var bytes = new byte[6 + frames.Length * 16];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), idType);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), (ushort)frames.Length);

        for (int i = 0; i < frames.Length; i++)
        {
            int offset = 6 + i * 16;
            bytes[offset] = frames[i].Width;
            bytes[offset + 1] = frames[i].Height;
        }
        return bytes;
    }
}
