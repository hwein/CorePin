using System.Globalization;
using System.Text;
using CorePin.Core.Topology;

namespace CorePin.Tests;

/// Format, serialisation and round trip.
public static class TopologyJsonTests
{
    private static readonly string[] AllFixtures =
        [Fixtures.Ryzen7945HX, Fixtures.Core14900K, Fixtures.CoreUltra155H, Fixtures.Phoenix2];

    /// The only safeguard of byte-for-byte serializer stability. The expectation stands
    /// in C#, never in a golden file.
    public static void Test_SerializeProducesExactText()
    {
        var snapshot = Fixtures.Snapshot(
            "AuthenticAMD",
            [Fixtures.MakeCore(0x3)],
            [Fixtures.MakeCache(0xFFFF, 33_554_432)],
            constructed: false);

        string expected =
            "{\n"
            + "  \"capturedBy\": \"CorePin 0.1.0\",\n"
            + "  \"constructed\": false,\n"
            + "  \"vendor\": \"AuthenticAMD\",\n"
            + "  \"cpuName\": \"Test CPU\",\n"
            + "  \"activeGroupCount\": 1,\n"
            + "  \"cores\": [\n"
            + "    { \"mask\": \"0x0000000000000003\", \"efficiencyClass\": 0, \"smt\": true }\n"
            + "  ],\n"
            + "  \"caches\": [\n"
            + "    { \"level\": 3, \"sizeBytes\": 33554432, \"mask\": \"0x000000000000FFFF\" }\n"
            + "  ]\n"
            + "}\n";

        Assert.Equal(expected, TopologyJson.Serialize(snapshot),
            "the canonical form is exact, down to the trailing newline");
    }

    public static void Test_RoundTripIsIdentical()
    {
        foreach (string name in AllFixtures)
        {
            string once = TopologyJson.Serialize(Fixtures.Load(name));
            string twice = TopologyJson.Serialize(TopologyJson.Parse(once));
            Assert.Equal(once, twice, $"serialising {name} is idempotent");
        }
    }

    /// The fourth fixture is excluded: it carries a header comment and the
    /// serializer writes no comments.
    public static void Test_ThreeDumpsAreCanonical()
    {
        // Compared as BYTES: File.ReadAllText strips a UTF-8 BOM silently, which would
        // leave the "UTF-8, no BOM" requirement untested.
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        foreach (string name in new[] { Fixtures.Ryzen7945HX, Fixtures.Core14900K, Fixtures.CoreUltra155H })
        {
            byte[] onDisk = Fixtures.Bytes(name);
            string canonical = TopologyJson.Serialize(TopologyJson.Parse(utf8NoBom.GetString(onDisk)));

            Assert.Equal(Convert.ToHexString(onDisk), Convert.ToHexString(utf8NoBom.GetBytes(canonical)),
                $"{name} is already in canonical form, byte for byte (LF, no BOM)");
        }
    }

    public static void Test_ParserToleratesCommentsAndTrailingCommas()
    {
        var withComment = Fixtures.Load(Fixtures.Phoenix2);
        Assert.Equal(6, withComment.Cores.Count, "the header comment does not stop the parser");

        string withTrailingComma =
            "{ \"capturedBy\": \"CorePin 0.1.0\", \"constructed\": true, \"vendor\": \"AuthenticAMD\","
            + " \"cpuName\": \"Test CPU\", \"activeGroupCount\": 1,"
            + " \"cores\": [{ \"mask\": \"0x3\", \"efficiencyClass\": 0, \"smt\": true },],"
            + " \"caches\": [], }";
        Assert.Equal(1, TopologyJson.Parse(withTrailingComma).Cores.Count,
            "a trailing comma is accepted");
    }

    public static void Test_ParserReadsShortMasks()
    {
        foreach (string form in new[] { "0x3", "0X3", "3", "0x0000000000000003" })
        {
            var snapshot = TopologyJson.Parse(MinimalJson(coreMask: form));
            Assert.Equal(3UL, snapshot.Cores[0].Mask, $"mask form '{form}' reads as 3");
        }
    }

    public static void Test_ParserThrowsOnMissingField()
    {
        foreach (string field in MandatoryFields)
        {
            string json = MinimalJson(omit: field);
            try
            {
                TopologyJson.Parse(json);
                throw new AssertionException($"missing field '{field}' must be rejected");
            }
            catch (TopologyFormatException ex)
            {
                Assert.True(ex.Message.Contains(field, StringComparison.Ordinal),
                    $"the message must name the missing field '{field}', was: {ex.Message}");
            }
        }
    }

    public static void Test_ParserIgnoresUnknownFields()
    {
        string json =
            "{ \"capturedBy\": \"CorePin 0.1.0\", \"constructed\": true, \"vendor\": \"AuthenticAMD\","
            + " \"cpuName\": \"Test CPU\", \"activeGroupCount\": 1, \"futureField\": 42,"
            + " \"cores\": [{ \"mask\": \"0x3\", \"efficiencyClass\": 0, \"smt\": true, \"future\": \"x\" }],"
            + " \"caches\": [{ \"level\": 3, \"sizeBytes\": 1, \"mask\": \"0x3\" }] }";

        Assert.Equal(TopologyJson.Serialize(TopologyJson.Parse(MinimalJson())),
                     TopologyJson.Serialize(TopologyJson.Parse(json)),
                     "extra fields on both levels change nothing");
    }

    /// The comma-as-decimal-separator trap. InvariantGlobalization is deliberately NOT
    /// set, so de-DE really can be created here.
    public static void Test_NoCultureDependency()
    {
        var snapshot = Fixtures.Load(Fixtures.Ryzen7945HX);
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string invariant = TopologyJson.Serialize(snapshot);

            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            string german = TopologyJson.Serialize(snapshot);

            Assert.Equal(invariant, german, "serialisation is culture independent");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    /// Seven top-level fields plus the five record fields.
    private static readonly string[] MandatoryFields =
        ["capturedBy", "constructed", "vendor", "cpuName", "activeGroupCount", "cores", "caches",
         "mask", "efficiencyClass", "smt", "level", "sizeBytes"];

    private static string MinimalJson(string? omit = null, string coreMask = "0x3")
    {
        (string Name, string Text)[] top =
        [
            ("capturedBy", "\"capturedBy\": \"CorePin 0.1.0\""),
            ("constructed", "\"constructed\": true"),
            ("vendor", "\"vendor\": \"AuthenticAMD\""),
            ("cpuName", "\"cpuName\": \"Test CPU\""),
            ("activeGroupCount", "\"activeGroupCount\": 1"),
        ];

        (string Name, string Text)[] core =
        [
            ("mask", $"\"mask\": \"{coreMask}\""),
            ("efficiencyClass", "\"efficiencyClass\": 0"),
            ("smt", "\"smt\": true"),
        ];

        (string Name, string Text)[] cache =
        [
            ("level", "\"level\": 3"),
            ("sizeBytes", "\"sizeBytes\": 1"),
            ("mask", "\"mask\": \"0x3\""),
        ];

        // "mask" exists on both record types; omitting it removes it from the core record,
        // which is enough to prove the parser rejects it.
        string coreText = "\"cores\": [{ " + Join(core, omit) + " }]";
        string cacheText = "\"caches\": [{ " + Join(cache, omit == "mask" ? null : omit) + " }]";

        var parts = new List<string>();
        foreach (var (name, text) in top) if (name != omit) parts.Add(text);
        if (omit != "cores") parts.Add(coreText);
        if (omit != "caches") parts.Add(cacheText);
        return "{ " + string.Join(", ", parts) + " }";
    }

    private static string Join((string Name, string Text)[] fields, string? omit)
        => string.Join(", ", fields.Where(f => f.Name != omit).Select(f => f.Text));
}
