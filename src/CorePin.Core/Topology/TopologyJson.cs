using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CorePin.Core.Primitives;

namespace CorePin.Core.Topology;

/// The one serializer/parser for the dump format. Output is canonical, input is tolerant.
public static class TopologyJson
{
    /// The default encoder would mask "+", "&" and "<"; only free-text fields are escaped.
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(TopologySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // MEASURED: Utf8JsonWriter.WriteRawValue does not indent, hence the StringBuilder.
        var text = new StringBuilder();
        text.Append("{\n");
        text.Append("  \"capturedBy\": \"").Append(Escape(snapshot.CapturedBy)).Append("\",\n");
        text.Append("  \"constructed\": ").Append(snapshot.Constructed ? "true" : "false").Append(",\n");
        text.Append("  \"vendor\": \"").Append(Escape(snapshot.Vendor)).Append("\",\n");
        text.Append("  \"cpuName\": \"").Append(Escape(snapshot.CpuName)).Append("\",\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"activeGroupCount\": {snapshot.ActiveGroupCount},\n");

        AppendArray(text, "cores", snapshot.Cores.Select(CoreLine));
        text.Append(",\n");
        AppendArray(text, "caches", snapshot.Caches.Select(CacheLine));

        // Exactly one trailing "\n" makes file and clipboard comparable.
        text.Append("\n}\n");
        return text.ToString();
    }

    private static void AppendArray(StringBuilder text, string name, IEnumerable<string> lines)
    {
        text.Append("  \"").Append(name).Append("\": [");
        bool first = true;
        foreach (string line in lines)
        {
            text.Append(first ? "\n" : ",\n").Append("    ").Append(line);
            first = false;
        }
        if (!first) text.Append("\n  ");
        text.Append(']');
    }

    private static string Escape(string value) => JsonEncodedText.Encode(value, Encoder).ToString();

    public static TopologySnapshot Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ReaderOptions);
        }
        catch (JsonException ex)
        {
            throw new TopologyFormatException($"not well-formed JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new TopologyFormatException("root element must be a JSON object");

            return new TopologySnapshot
            {
                CapturedBy = ReadString(root, "capturedBy"),
                Constructed = ReadBool(root, "constructed"),
                Vendor = ReadString(root, "vendor"),
                CpuName = ReadString(root, "cpuName"),
                ActiveGroupCount = ReadInt(root, "activeGroupCount"),
                Cores = ReadArray(root, "cores", ReadCore),
                Caches = ReadArray(root, "caches", ReadCache),
            };
        }
    }

    private static string CoreLine(CoreRecord core)
        => string.Create(CultureInfo.InvariantCulture,
            $$"""{ "mask": "{{AffinityMask.ToHex(core.Mask)}}", "efficiencyClass": {{core.EfficiencyClass}}, "smt": {{(core.Smt ? "true" : "false")}} }""");

    private static string CacheLine(CacheRecord cache)
        => string.Create(CultureInfo.InvariantCulture,
            $$"""{ "level": {{cache.Level}}, "sizeBytes": {{cache.SizeBytes}}, "mask": "{{AffinityMask.ToHex(cache.Mask)}}" }""");

    private static CoreRecord ReadCore(JsonElement element, string path) => new()
    {
        Mask = ReadMask(element, "mask", $"{path}.mask"),
        EfficiencyClass = ReadInt(element, "efficiencyClass", $"{path}.efficiencyClass"),
        Smt = ReadBool(element, "smt", $"{path}.smt"),
    };

    private static CacheRecord ReadCache(JsonElement element, string path) => new()
    {
        Level = ReadInt(element, "level", $"{path}.level"),
        SizeBytes = ReadLong(element, "sizeBytes", $"{path}.sizeBytes"),
        Mask = ReadMask(element, "mask", $"{path}.mask"),
    };

    private static List<T> ReadArray<T>(
        JsonElement parent, string name, Func<JsonElement, string, T> readItem)
    {
        var element = Require(parent, name, name);
        if (element.ValueKind != JsonValueKind.Array)
            throw new TopologyFormatException($"field '{name}' must be an array");

        var result = new List<T>();
        int index = 0;
        foreach (var item in element.EnumerateArray())
        {
            string path = $"{name}[{index}]";
            if (item.ValueKind != JsonValueKind.Object)
                throw new TopologyFormatException($"field '{path}' must be an object");
            result.Add(readItem(item, path));
            index++;
        }
        return result;
    }

    private static JsonElement Require(JsonElement parent, string name, string path)
        => parent.TryGetProperty(name, out var value)
            ? value
            : throw new TopologyFormatException($"field '{path}' is missing");

    private static string ReadString(JsonElement parent, string name)
    {
        var value = Require(parent, name, name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw new TopologyFormatException($"field '{name}' must be a string");
    }

    private static bool ReadBool(JsonElement parent, string name, string? path = null)
    {
        var value = Require(parent, name, path ?? name);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new TopologyFormatException($"field '{path ?? name}' must be a boolean"),
        };
    }

    private static int ReadInt(JsonElement parent, string name, string? path = null)
    {
        var value = Require(parent, name, path ?? name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            throw new TopologyFormatException($"field '{path ?? name}' must be an integer");
        return result;
    }

    private static long ReadLong(JsonElement parent, string name, string? path = null)
    {
        var value = Require(parent, name, path ?? name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long result))
            throw new TopologyFormatException($"field '{path ?? name}' must be an integer");
        return result;
    }

    private static ulong ReadMask(JsonElement parent, string name, string path)
    {
        var value = Require(parent, name, path);
        if (value.ValueKind != JsonValueKind.String)
            throw new TopologyFormatException($"field '{path}' must be a string");

        string text = (value.GetString() ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        if (text.Length == 0 || text.Length > 16
            || !ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong mask))
            throw new TopologyFormatException($"field '{path}' is not a 64-bit hex mask");

        return mask;
    }
}
