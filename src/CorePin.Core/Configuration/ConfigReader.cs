using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using CorePin.Core.Diagnostics;

namespace CorePin.Core.Configuration;

/// Caught inside ConfigStore.Load and turned into ConfigLoadOutcome.Corrupt; never leaves it.
internal sealed class ConfigFormatException(string field) : Exception(field);

internal sealed record ConfigHeader(
    int SchemaVersion, MachineInfo Machine, Settings Settings, JsonElement[]? Rules);

/// Everything outside `rules`: a wrong type is a format error, a bad value is clamped.
internal sealed class ConfigReader(ILog log)
{
    private const int MaxLogicalProcessors = 64;
    private const int MinPollIntervalMs = 500;
    private const int MaxPollIntervalMs = 5000;
    private const int MinWindowWidth = 560;
    private const int MinWindowHeight = 420;

    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly HashSet<string> KnownTopLevelFields =
        new(["schemaVersion", "machine", "settings", "rules"], StringComparer.Ordinal);

    private static readonly HashSet<string> KnownSettingsFields =
        new(["pollIntervalMs", "startWithWindows", "logLevel", "windowBounds"], StringComparer.Ordinal);

    public bool TryRead(string text, [NotNullWhen(true)] out ConfigHeader? header)
    {
        try
        {
            var dto = JsonSerializer.Deserialize(text, ConfigJsonContext.Default.ConfigFileDto)
                      ?? throw new ConfigFormatException("root");
            header = new ConfigHeader(
                ReadSchemaVersion(dto), ReadMachine(dto.Machine), ReadSettings(dto.Settings), dto.Rules);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ConfigFormatException)
        {
            header = null;
            return false;
        }
    }

    /// Top level and `settings` only: a future additive field inside a rule must not warn.
    public void ReportUnknownFields(string text)
    {
        var names = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(text, ReaderOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!KnownTopLevelFields.Contains(property.Name)) names.Add(property.Name);
            }

            if (document.RootElement.TryGetProperty("settings", out var settings)
                && settings.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in settings.EnumerateObject())
                {
                    if (!KnownSettingsFields.Contains(property.Name)) names.Add("settings." + property.Name);
                }
            }
        }
        catch (JsonException)
        {
            return;     // the same text already parsed once; nothing to report if it does not now
        }

        if (names.Count > 0)
            log.Warn("config", $"unknown field(s) ignored: {string.Join(", ", names)}");
    }

    private enum RawIntKind { Missing, Value, WrongType }

    /// A missing key (Undefined) is NOT a type error — a partial file is the normal case.
    private static RawIntKind ReadRawInt(JsonElement element, out int value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Undefined:
                value = default;
                return RawIntKind.Missing;
            case JsonValueKind.Number when element.TryGetInt32(out value):
                return RawIntKind.Value;
            default:
                value = default;
                return RawIntKind.WrongType;
        }
    }

    private static int ReadSchemaVersion(ConfigFileDto dto) => ReadRawInt(dto.SchemaVersion, out int raw) switch
    {
        RawIntKind.Missing => 1,
        RawIntKind.Value => raw < 1 ? 1 : raw,
        RawIntKind.WrongType => throw new ConfigFormatException("schemaVersion"),
        _ => throw new UnreachableException(),
    };

    private static MachineInfo ReadMachine(MachineJson? machine)
    {
        if (machine is null) return new MachineInfo("", 0);

        int logicalProcessors = ReadRawInt(machine.LogicalProcessors, out int raw) switch
        {
            RawIntKind.Missing => 0,
            RawIntKind.Value => raw is >= 1 and <= MaxLogicalProcessors ? raw : 0,
            RawIntKind.WrongType => throw new ConfigFormatException("machine.logicalProcessors"),
            _ => throw new UnreachableException(),
        };

        // cpuName is never checked, only displayed.
        return new MachineInfo(machine.CpuName ?? "", logicalProcessors);
    }

    private Settings ReadSettings(SettingsJson? settings)
    {
        if (settings is null) return new Settings();

        int pollIntervalMs = ReadRawInt(settings.PollIntervalMs, out int raw) switch
        {
            RawIntKind.Missing => 1000,
            RawIntKind.Value => ClampPollInterval(raw),
            RawIntKind.WrongType => throw new ConfigFormatException("settings.pollIntervalMs"),
            _ => throw new UnreachableException(),
        };

        return new Settings
        {
            PollIntervalMs = pollIntervalMs,
            StartWithWindows = settings.StartWithWindows ?? "normal",
            LogLevel = ReadLogLevel(settings.LogLevel),
            WindowBounds = ReadWindowBounds(settings.WindowBounds),
        };
    }

    private int ClampPollInterval(int value)
    {
        if (value >= MinPollIntervalMs && value <= MaxPollIntervalMs) return value;

        int clamped = value < MinPollIntervalMs ? MinPollIntervalMs : MaxPollIntervalMs;
        log.Warn("config", $"pollIntervalMs {value} out of range, clamped to {clamped}");
        return clamped;
    }

    private LogLevel ReadLogLevel(string? value)
    {
        if (value is null) return LogLevel.Info;
        if (string.Equals(value, "debug", StringComparison.OrdinalIgnoreCase)) return LogLevel.Debug;
        if (string.Equals(value, "info", StringComparison.OrdinalIgnoreCase)) return LogLevel.Info;
        if (string.Equals(value, "warn", StringComparison.OrdinalIgnoreCase)) return LogLevel.Warn;

        log.Warn("config",
            $"settings.logLevel '{value}' is not a valid level (debug|info|warn), using info");
        return LogLevel.Info;
    }

    private WindowBounds? ReadWindowBounds(WindowBoundsJson? bounds)
    {
        if (bounds is null) return null;

        if (bounds.W < MinWindowWidth || bounds.H < MinWindowHeight)
        {
            log.Warn("config", $"windowBounds {bounds.W}x{bounds.H} below minimum size, discarded");
            return null;
        }
        return new WindowBounds(bounds.X, bounds.Y, bounds.W, bounds.H);
    }
}
