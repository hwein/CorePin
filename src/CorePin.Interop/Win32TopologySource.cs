using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using CorePin.Core.Platform;
using CorePin.Core.Topology;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// Raw topology capture over GetLogicalProcessorInformationEx (S04 §2). Public and
/// parameterless: the debug path in S01 §3.7 creates a second instance, and the class
/// holds no state beyond a single Read() (S01 §3.8).
///
/// The source does not log — it runs in S01 §3.7 step 0, the logger only appears in
/// step 3. Anomalies leave as DATA via Warnings (S04 §2.7, T23).
public sealed class Win32TopologySource : ITopologySource
{
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public TopologySnapshot Read()
    {
        _warnings.Clear();
        var (buffer, length) = Query(_warnings);

        // Read before the walk, reported after it — the warning order of §2.7 is the order
        // of occurrence, and the buffer anomalies happen first.
        var identity = CpuIdentity.Read();

        var snapshot = ParseBuffer(buffer.AsSpan(0, (int)length),
                                   CapturedBy(), identity.Vendor, identity.CpuName, _warnings);

        if (identity.Vendor.Length == 0) _warnings.Add("registry: VendorIdentifier empty");
        if (identity.CpuName.Length == 0) _warnings.Add("registry: ProcessorNameString empty");

        return snapshot;
    }

    /// Only for --debug-dump-raw (S04 §8.4). This is a SECOND Win32 call, so the bytes are
    /// not guaranteed to be identical to the ones the dump was built from.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1822:Mark members as static",
        Justification = "S04 §2.1 declares ReadRawBuffer as an instance member of the source; "
                      + "DumpTopologyCommand calls it on the ITopologySource it already holds.")]
    public byte[] ReadRawBuffer()
    {
        var (buffer, length) = Query([]);
        return buffer.AsSpan(0, (int)length).ToArray();
    }

    // ── The two-step call (S04 §2.1) ────────────────────────────────────────────────

    private static (byte[] Buffer, uint Length) Query(List<string> warnings)
    {
        uint length = 0;
        if (GetLogicalProcessorInformationEx(
                LogicalProcessorRelationship.RelationAll, nint.Zero, ref length))
        {
            throw new TopologyReadException(
                "GetLogicalProcessorInformationEx unexpectedly succeeded on the size query");
        }

        int error = Marshal.GetLastPInvokeError();
        if (error != Win32Error.INSUFFICIENT_BUFFER)
            throw new TopologyReadException("GetLogicalProcessorInformationEx size query failed", error);
        if (length == 0)
            throw new TopologyReadException("GetLogicalProcessorInformationEx reported a zero-length buffer");

        // At most one retry: an unbounded loop here would be a hang during startup, and two
        // attempts cover every real change (S04 §2.1 step 3).
        for (int attempt = 0; ; attempt++)
        {
            byte[] buffer = new byte[length];
            uint written = length;

            // A managed byte[] pinned for the duration of the call: no unsafe, no leak on
            // an exception between allocation and release (S04 T24).
            var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            bool ok;
            try
            {
                ok = GetLogicalProcessorInformationEx(
                    LogicalProcessorRelationship.RelationAll, pin.AddrOfPinnedObject(), ref written);
                error = ok ? 0 : Marshal.GetLastPInvokeError();
            }
            finally { pin.Free(); }

            if (ok)
            {
                if (written == 0)
                    throw new TopologyReadException("GetLogicalProcessorInformationEx returned no records");
                if (attempt > 0) warnings.Add("buffer grew between calls, retried once");
                return (buffer, written);
            }

            if (error != Win32Error.INSUFFICIENT_BUFFER || attempt > 0)
                throw new TopologyReadException("GetLogicalProcessorInformationEx failed", error);

            length = written;                          // the topology changed between calls
        }
    }

    private static string CapturedBy()
    {
        string? informational = typeof(Win32TopologySource).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational)) return "CorePin 0.0.0";

        // The SDK appends "+<commit>" when a source revision is known.
        int plus = informational.IndexOf('+', StringComparison.Ordinal);
        return "CorePin " + (plus < 0 ? informational : informational[..plus]);
    }

    // ── The buffer walk over variable-length records (S04 §2.2/§2.3) ────────────────

    private const int HeaderSize = 8;
    private const int GroupAffinitySize = 16;
    private const int ProcessorGroupInfoSize = 48;

    /// Separated from the Win32 call so the recorded buffer of S01 §5.5 can be fed in
    /// without a native call (S01 §2.1, the interop test boundary).
    internal static TopologySnapshot ParseBuffer(
        ReadOnlySpan<byte> span, string capturedBy, string vendor, string cpuName, List<string> warnings)
    {
        if (span.Length == 0)
            throw new TopologyReadException("topology buffer is empty");

        var cores = new List<CoreRecord>();
        var caches = new List<CacheRecord>();
        int? activeGroupCount = null;
        ulong activeProcessorMask = 0;

        int offset = 0;
        while (offset < span.Length)
        {
            if (span.Length - offset < HeaderSize)
                throw new TopologyReadException(Invariant($"record header truncated at offset {offset}"));

            uint relationship = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4, 4));

            // Never skip and continue: that is the off-by-one of 02 §12.1, and "repairing"
            // it by searching on produces records made of random bytes.
            if (size < HeaderSize || size > (uint)(span.Length - offset))
                throw new TopologyReadException(
                    Invariant($"record size {size} at offset {offset} (rel {relationship})"));

            var record = span.Slice(offset, (int)size);
            switch ((LogicalProcessorRelationship)relationship)
            {
                case LogicalProcessorRelationship.RelationProcessorCore:
                    ReadProcessorCore(record, offset, cores, warnings);
                    break;
                case LogicalProcessorRelationship.RelationCache:
                    ReadCache(record, offset, caches, warnings);
                    break;
                case LogicalProcessorRelationship.RelationGroup:
                    ReadGroup(record, offset, out activeGroupCount, out activeProcessorMask);
                    break;
                default:
                    break;                              // unknown relationships are skipped
            }

            offset += (int)size;
        }

        // Defaulting to 1 would let a multi-group system through silently and set masks
        // that mean something else there (02 §4.5, prior-round finding 5).
        if (activeGroupCount is not { } groups)
            throw new TopologyReadException("no RelationGroup record in the topology buffer");

        if (groups == 1)
        {
            ulong union = 0;
            foreach (var core in cores) union |= core.Mask;
            if (union != activeProcessorMask)
                warnings.Add("core mask union != ActiveProcessorMask");
        }

        return new TopologySnapshot
        {
            CapturedBy = capturedBy,
            Constructed = false,                        // a run on real hardware IS a measurement
            Vendor = vendor,
            CpuName = cpuName,
            ActiveGroupCount = groups,
            Cores = cores,
            Caches = caches,
        };
    }

    private static void ReadProcessorCore(
        ReadOnlySpan<byte> record, int offset, List<CoreRecord> cores, List<string> warnings)
    {
        Guard(record.Length >= 32, offset, LogicalProcessorRelationship.RelationProcessorCore, record.Length);

        byte flags = record[8];
        int efficiencyClass = record[9];
        int count = GroupCount(BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(30, 2)), offset, warnings);

        Guard(record.Length >= 32 + GroupAffinitySize * count,
              offset, LogicalProcessorRelationship.RelationProcessorCore, record.Length);

        for (int i = 0; i < count; i++)
        {
            cores.Add(new CoreRecord
            {
                Mask = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(32 + GroupAffinitySize * i, 8)),
                EfficiencyClass = efficiencyClass,
                // A bit test, not an equality comparison: Flags == LTP_PC_SMT would turn
                // Smt false as soon as Windows sets a second flag (S04 §2.5).
                Smt = (flags & LTP_PC_SMT) != 0,
            });
        }
    }

    private static void ReadCache(
        ReadOnlySpan<byte> record, int offset, List<CacheRecord> caches, List<string> warnings)
    {
        // 40, not 32: GroupCount sits at offset 38 and ends at byte 40 (S04 §2.2).
        Guard(record.Length >= 40, offset, LogicalProcessorRelationship.RelationCache, record.Length);

        int level = record[8];
        uint sizeBytes = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(12, 4));
        int count = GroupCount(BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(38, 2)), offset, warnings);

        Guard(record.Length >= 40 + GroupAffinitySize * count,
              offset, LogicalProcessorRelationship.RelationCache, record.Length);

        for (int i = 0; i < count; i++)
        {
            // No filter on Type: the core filters on level == 3; a CacheUnified filter here
            // would be an assumption about how an L3 is reported (S04 §2.5).
            caches.Add(new CacheRecord
            {
                Level = level,
                SizeBytes = sizeBytes,
                Mask = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(40 + GroupAffinitySize * i, 8)),
            });
        }
    }

    private static void ReadGroup(
        ReadOnlySpan<byte> record, int offset, out int? activeGroupCount, out ulong activeProcessorMask)
    {
        Guard(record.Length >= 12, offset, LogicalProcessorRelationship.RelationGroup, record.Length);

        int active = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(10, 2));

        // The second guard is larger than ActiveGroupCount alone would need, because
        // GroupInfo[0].ActiveProcessorMask is read too (S04 §2.2/§2.4).
        Guard(record.Length >= 32 + ProcessorGroupInfoSize * active,
              offset, LogicalProcessorRelationship.RelationGroup, record.Length);

        activeGroupCount = active;
        activeProcessorMask = active >= 1
            ? BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(32 + 40, 8))
            : 0;
    }

    /// GroupCount == 0 can only come from a Windows version that still carried the field as
    /// reserved; both header generations put the first GROUP_AFFINITY at the same offset,
    /// and there is exactly one entry there (S04 §2.3, T-A2).
    private static int GroupCount(ushort raw, int offset, List<string> warnings)
    {
        if (raw != 0) return raw;
        warnings.Add(Invariant($"groupcount 0 at offset {offset}, assuming 1"));
        return 1;
    }

    private static void Guard(bool ok, int offset, LogicalProcessorRelationship relationship, int size)
    {
        if (ok) return;
        throw new TopologyReadException(Invariant(
            $"record at offset {offset} (rel {(uint)relationship}) is {size} bytes, too short for its relationship"));
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
