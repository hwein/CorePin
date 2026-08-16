using System.Text.Json;
using System.Text.Json.Serialization;

namespace CorePin.Core.Configuration;

// ── Stage 1: the outer shell. NO required member. `rules` stays raw.
internal sealed class ConfigFileDto
{
    public JsonElement SchemaVersion { get; set; }
    public MachineJson? Machine { get; set; }
    public SettingsJson? Settings { get; set; }
    public JsonElement[]? Rules { get; set; }
}

internal sealed class MachineJson
{
    public string? CpuName { get; set; }
    public JsonElement LogicalProcessors { get; set; }
}

internal sealed class SettingsJson
{
    public JsonElement PollIntervalMs { get; set; }
    public string? StartWithWindows { get; set; }
    public string? LogLevel { get; set; }
    public WindowBoundsJson? WindowBounds { get; set; }
}

internal sealed class WindowBoundsJson
{
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}

// ── Stage 2: one rule at a time, likewise without required members.
internal sealed class RuleJson
{
    public string? Id { get; set; }
    public string? ExeName { get; set; }
    public string? LastKnownPath { get; set; }
    public int[]? Threads { get; set; }

    /// Raw, not bool?: a bool? would throw in Deserialize and read as `malformed entry`.
    public JsonElement Enabled { get; set; }
}

// ── Writing: own, strict DTOs, separate from the tolerant reading ones.
internal sealed class ConfigFileWriteDto
{
    public required int SchemaVersion { get; init; }
    public required MachineWriteDto Machine { get; init; }
    public required SettingsWriteDto Settings { get; init; }
    public required RuleWriteDto[] Rules { get; init; }
}

internal sealed class MachineWriteDto
{
    public required string CpuName { get; init; }
    public required int LogicalProcessors { get; init; }
}

internal sealed class SettingsWriteDto
{
    public required int PollIntervalMs { get; init; }
    public required string StartWithWindows { get; init; }
    public WindowBoundsJson? WindowBounds { get; init; }
    public required string LogLevel { get; init; }
}

/// Deliberately carries NO NeedsReview field — it is never persisted.
internal sealed class RuleWriteDto
{
    public required string Id { get; init; }
    public required string ExeName { get; init; }
    public string? LastKnownPath { get; init; }
    public required int[] Threads { get; init; }
    public required bool Enabled { get; init; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    IndentSize = 2,
    NewLine = "\n",
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ConfigFileDto))]
[JsonSerializable(typeof(RuleJson))]
[JsonSerializable(typeof(ConfigFileWriteDto))]
[JsonSerializable(typeof(JsonElement[]))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext
{
}
