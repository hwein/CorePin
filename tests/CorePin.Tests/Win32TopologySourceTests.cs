using System.Buffers.Binary;
using CorePin.Core.Platform;
using CorePin.Core.Topology;
using CorePin.Interop;

namespace CorePin.Tests;

/// Tests for the buffer walk — free of native calls, like every CorePin.Interop test.
public static class Win32TopologySourceTests
{
    private const string RecordedBuffer = "ryzen-9-7945hx.buffer.b64";

    /// Recorded, not constructed: a hand-built buffer would use the same offset
    /// assumptions as the parser and check the parser against itself.
    public static void Test_RecordedBufferMatchesFrozenDump()
    {
        var expected = Fixtures.Load(Fixtures.Ryzen7945HX);
        var warnings = new List<string>();

        var actual = Parse(Fixtures.RawBuffer(RecordedBuffer),
                           expected.CapturedBy, expected.Vendor, expected.CpuName, warnings);

        Assert.Equal(TopologyJson.Serialize(expected), TopologyJson.Serialize(actual),
            "the buffer walk reproduces the frozen dump of the development machine");
        Assert.Equal("", string.Join(";", warnings), "the recorded buffer produces no warnings");
    }

    /// A RelationCache record with Size == 24 lies fully inside the buffer, but GroupCount
    /// (offset 38) and the first GROUP_AFFINITY (40–55) would come from the FOLLOWING
    /// record — a silently wrong cache mask. Here it is checked that the walk does NOT read.
    public static void Test_WalkRejectsTooShortCacheRecord()
    {
        byte[] buffer = Record(relationship: 2, size: 24);
        Assert.Throws<TopologyReadException>(() => Parse(buffer),
            "a RelationCache record of 24 bytes is rejected instead of read across the boundary");
    }

    /// Separates the guard 40 from a wrong 32: a 24-byte record fails either way, so only
    /// Size == 32 shows that GroupCount (offset 38–39) still lies outside the record.
    public static void Test_WalkRejectsCacheRecordOfExactlyThirtyTwoBytes()
    {
        byte[] buffer = Record(relationship: 2, size: 32);
        Assert.Throws<TopologyReadException>(() => Parse(buffer),
            "a RelationCache record of 32 bytes is rejected — GroupCount ends at byte 40");
    }

    public static void Test_WalkRejectsZeroSize()
    {
        byte[] buffer = Record(relationship: 0, size: 0, totalLength: 32);
        Assert.Throws<TopologyReadException>(() => Parse(buffer),
            "Size == 0 ends in an exception, not in an endless loop");
    }

    private static TopologySnapshot Parse(
        byte[] buffer, string capturedBy = "CorePin 0.1.0", string vendor = "AuthenticAMD",
        string cpuName = "Test CPU", List<string>? warnings = null)
        => Win32TopologySource.ParseBuffer(buffer, capturedBy, vendor, cpuName, warnings ?? []);

    private static byte[] Record(uint relationship, uint size, int? totalLength = null)
    {
        var buffer = new byte[totalLength ?? (int)size];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), relationship);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), size);
        return buffer;
    }
}
