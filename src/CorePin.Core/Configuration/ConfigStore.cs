using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CorePin.Core.Diagnostics;
using CorePin.Core.Primitives;
using CorePin.Core.Rules;
using CorePin.Core.Time;

namespace CorePin.Core.Configuration;

/// Loads and writes config.json. Load() and Save() never throw; Save is not debounced.
public sealed class ConfigStore
{
    private const string FileName = "config.json";
    private const string TempFileName = "config.json.tmp";
    private const string SkippedFileName = "config.skipped.json";
    private const int MaxSchemaVersion = 1;
    private const int MinPollIntervalMs = 500;
    private const int MaxPollIntervalMs = 5000;
    private const int MaxLogicalProcessors = 64;
    private const int MinWindowWidth = 560;
    private const int MinWindowHeight = 420;
    private const int MaxThreadIndex = 63;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// No Encoder in [JsonSourceGenerationOptions]; the options overload warns IL2026/IL3050.
    private static readonly ConfigJsonContext WriteContext =
        new(new JsonSerializerOptions(ConfigJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly HashSet<string> KnownTopLevelFields =
        new(["schemaVersion", "machine", "settings", "rules"], StringComparer.Ordinal);

    private static readonly HashSet<string> KnownSettingsFields =
        new(["pollIntervalMs", "startWithWindows", "logLevel", "windowBounds"], StringComparer.Ordinal);

    private readonly string _directory;
    private readonly ILog _log;
    private readonly IClock _clock;

    private WriteGuard _guard = WriteGuard.Open;

    public ConfigStore(string directory, ILog log, IClock clock)
    {
        _directory = directory;
        _log = log;
        _clock = clock;
    }

    /// The logicalProcessors comparison and Rule.NeedsReview belong to the composition root.
    public ConfigLoadResult Load()
    {
        if (!TryEnsureDirectory())
        {
            _log.Warn("config", "cannot create config directory, starting with in-memory defaults");
            return Defaults(ConfigLoadOutcome.Missing);
        }

        string path = Path.Combine(_directory, FileName);
        if (!File.Exists(path))
        {
            _log.Info("config", "no config.json found, starting with defaults");
            return Defaults(ConfigLoadOutcome.Missing);
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            // Untouched on purpose: a Missing here would later overwrite the real rules.
            _log.Warn("config",
                $"config.json exists but could not be opened ({ex.GetType().Name}), starting with defaults; changes will not be saved");
            return Defaults(ConfigLoadOutcome.Unreadable);
        }

        ConfigFileDto dto;
        int schemaVersion;
        MachineInfo machine;
        Settings settings;
        try
        {
            dto = JsonSerializer.Deserialize(text, ConfigJsonContext.Default.ConfigFileDto)
                  ?? throw new ConfigFormatException("root");
            schemaVersion = ReadSchemaVersion(dto);
            machine = ReadMachine(dto.Machine);
            settings = ReadSettings(dto.Settings);
        }
        catch (Exception ex) when (ex is JsonException or ConfigFormatException)
        {
            return Corrupt(path);
        }

        if (schemaVersion > MaxSchemaVersion)
        {
            // The file is not touched at all — its timestamp must stay unchanged.
            _log.Warn("config",
                $"config.json schemaVersion {schemaVersion} is newer than supported ({MaxSchemaVersion}), UI is read-only");
            return new ConfigLoadResult(AppConfig.Empty(), RuleSet.Empty, ConfigLoadOutcome.TooNew,
                                        schemaVersion.ToString(CultureInfo.InvariantCulture), 0);
        }

        ReportUnknownFields(text);

        var (rules, skippedRaw) = ParseRules(dto.Rules);
        string? detail = UpdateSkippedFile(skippedRaw);

        var config = new AppConfig
        {
            SchemaVersion = schemaVersion,
            Machine = machine,
            Settings = settings,
            Rules = RuleSet.Empty,          // always empty, structurally
        };

        _log.Info("config", $"config.json loaded, {rules.Rules.Count} rules, schemaVersion {schemaVersion}");
        return new ConfigLoadResult(config, rules, ConfigLoadOutcome.Loaded, detail, skippedRaw.Count);
    }

    /// PRECONDITION: config.Rules must come from the marked rule set. No-op while blocked.
    public void Save(AppConfig config)
    {
        if (!_guard.CanPersist)
        {
            _log.Warn("config", $"save discarded: WriteGuard.{_guard.Reason} active");
            return;
        }
        WriteWithRetry(config);
    }

    public void BlockWrites(GuardReason reason) => _guard = reason switch
    {
        GuardReason.ConfigTooNew => WriteGuard.ReadOnly,
        GuardReason.ConfigUnreadable => WriteGuard.Unreadable,
        GuardReason.DebugTopology => WriteGuard.NoPersist,
        _ => WriteGuard.Open,
    };

    // ── loading ─────────────────────────────────────────────────────────────────────

    private static ConfigLoadResult Defaults(ConfigLoadOutcome outcome)
        => new(AppConfig.Empty(), RuleSet.Empty, outcome, null, 0);

    private bool TryEnsureDirectory()
    {
        // AppPaths creates nothing; a failure here is a state, not an exception.
        try { Directory.CreateDirectory(_directory); return true; }
        catch (Exception) { return false; }
    }

    private ConfigLoadResult Corrupt(string path)
    {
        string name = FreeCorruptName();
        try { File.Move(path, Path.Combine(_directory, name)); }
        catch (Exception)
        {
            // If the rename fails, the file keeps its name and the outcome is still Corrupt.
        }

        _log.Warn("config", $"config.json unreadable, renamed to {name}, starting with defaults");
        return new ConfigLoadResult(AppConfig.Empty(), RuleSet.Empty, ConfigLoadOutcome.Corrupt, name, 0);
    }

    private string FreeCorruptName()
    {
        // Local time: a user looking at the file places it faster.
        string stamp = _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string candidate = $"config.corrupt-{stamp}.json";
        for (int suffix = 2; File.Exists(Path.Combine(_directory, candidate)); suffix++)
            candidate = $"config.corrupt-{stamp}-{suffix}.json";
        return candidate;
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
        _log.Warn("config", $"pollIntervalMs {value} out of range, clamped to {clamped}");
        return clamped;
    }

    private LogLevel ReadLogLevel(string? value)
    {
        if (value is null) return LogLevel.Info;
        if (string.Equals(value, "debug", StringComparison.OrdinalIgnoreCase)) return LogLevel.Debug;
        if (string.Equals(value, "info", StringComparison.OrdinalIgnoreCase)) return LogLevel.Info;
        if (string.Equals(value, "warn", StringComparison.OrdinalIgnoreCase)) return LogLevel.Warn;

        _log.Warn("config",
            $"settings.logLevel '{value}' is not a valid level (debug|info|warn), using info");
        return LogLevel.Info;
    }

    private WindowBounds? ReadWindowBounds(WindowBoundsJson? bounds)
    {
        if (bounds is null) return null;

        if (bounds.W < MinWindowWidth || bounds.H < MinWindowHeight)
        {
            _log.Warn("config", $"windowBounds {bounds.W}x{bounds.H} below minimum size, discarded");
            return null;
        }
        return new WindowBounds(bounds.X, bounds.Y, bounds.W, bounds.H);
    }

    /// Top level and `settings` only: a future additive field inside a rule must not warn.
    private void ReportUnknownFields(string text)
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
            _log.Warn("config", $"unknown field(s) ignored: {string.Join(", ", names)}");
    }

    private (RuleSet Rules, List<JsonElement> SkippedRaw) ParseRules(JsonElement[]? raw)
    {
        var result = new List<Rule>();
        var skippedRaw = new List<JsonElement>();
        var idToExeName = new Dictionary<Guid, string>();
        var exeNameToFirstId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        if (raw is null) return (RuleSet.Empty, skippedRaw);

        for (int i = 0; i < raw.Length; i++)
        {
            var element = raw[i];
            RuleJson? dto;
            try { dto = element.Deserialize(ConfigJsonContext.Default.RuleJson); }
            catch (JsonException)
            {
                _log.Warn("config", $"rule at index {i} skipped: malformed entry");
                skippedRaw.Add(element);
                continue;
            }

            if (!TryValidate(dto, out var rule, out string reason))
            {
                _log.Warn("config", $"rule at index {i} skipped: {reason}");
                skippedRaw.Add(element);
                continue;
            }

            // File order decides: on a duplicate id or exeName the FIRST rule wins.
            if (idToExeName.ContainsKey(rule.Id))
            {
                _log.Warn("config", $"rule '{rule.ExeName}' skipped: duplicate id {rule.Id}");
                skippedRaw.Add(element);
                continue;
            }
            if (exeNameToFirstId.TryGetValue(rule.ExeName, out var firstId))
            {
                _log.Warn("config",
                    $"rule '{rule.ExeName}' skipped: duplicate exeName, rule {firstId} already covers this program");
                skippedRaw.Add(element);
                continue;
            }

            idToExeName[rule.Id] = rule.ExeName;
            exeNameToFirstId[rule.ExeName] = rule.Id;
            result.Add(rule);
        }

        return (new RuleSet(result), skippedRaw);
    }

    private bool TryValidate(RuleJson? dto, [NotNullWhen(true)] out Rule? rule, out string reason)
    {
        rule = null;

        if (dto is null) { reason = "malformed entry"; return false; }

        if (!Guid.TryParse(dto.Id, out var id)) { reason = "missing/invalid id"; return false; }

        string exeName = (dto.ExeName ?? string.Empty).Trim();
        // `malformed entry` is the only one of the four skip reasons that fits an empty name.
        if (exeName.Length == 0) { reason = "malformed entry"; return false; }

        bool enabled;
        switch (dto.Enabled.ValueKind)
        {
            case JsonValueKind.Undefined: enabled = true; break;
            case JsonValueKind.True: enabled = true; break;
            case JsonValueKind.False: enabled = false; break;
            default: reason = "invalid enabled"; return false;
        }

        if (dto.Threads is not { Length: > 0 }) { reason = "empty threads"; return false; }

        var kept = new List<int>();
        foreach (int thread in dto.Threads)
        {
            if ((uint)thread > MaxThreadIndex)
            {
                _log.Warn("config", $"rule '{exeName}': thread index {thread} out of range, dropped");
                continue;
            }
            kept.Add(thread);
        }
        if (kept.Count == 0) { reason = "empty threads"; return false; }

        rule = new Rule
        {
            Id = id,
            ExeName = exeName,
            LastKnownPath = dto.LastKnownPath,
            Threads = AffinityMask.FromThreads(kept),     // duplicates collapse into one mask
            Enabled = enabled,
        };
        reason = string.Empty;
        return true;
    }

    /// The only file ConfigStore deletes on its own, and guard-free like the rename.
    private string? UpdateSkippedFile(List<JsonElement> skippedRaw)
    {
        string path = Path.Combine(_directory, SkippedFileName);

        if (skippedRaw.Count == 0)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    _log.Debug("config", "config.skipped.json removed, no rules skipped on this load");
                }
            }
            catch (Exception)
            {
                // Best effort: a leftover diagnostic file is not worth a failure.
            }
            return null;
        }

        try
        {
            string json = JsonSerializer.Serialize(skippedRaw.ToArray(), ConfigJsonContext.Default.JsonElementArray);
            File.WriteAllText(path, json, Utf8NoBom);
        }
        catch (Exception)
        {
            // RulesSkipped and Outcome stay correct, only the diagnostic file is missing.
        }
        return SkippedFileName;
    }

    // ── writing ─────────────────────────────────────────────────────────────────────

    private void WriteWithRetry(AppConfig config)
    {
        string json;
        try
        {
            json = Serialize(config);
        }
        catch (Exception ex)
        {
            // Serialization sits INSIDE the try: Save() must never throw. Not retryable.
            _log.Warn("config", $"write failed: {ex.GetType().Name} {ex.HResult}");
            return;
        }

        ReadOnlySpan<int> backoffMs = [50, 150, 400];
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                WriteAtomically(json);
                return;
            }
            catch (IOException ex) when (attempt < backoffMs.Length)
            {
                _log.Warn("config",
                    $"write attempt {attempt + 1} failed: {ex.GetType().Name} {ex.HResult}, retrying");
                Thread.Sleep(backoffMs[attempt]);
            }
            catch (Exception ex)
            {
                // Catch-all so Save never throws; no ex.Message — it can carry the user name.
                _log.Warn("config", $"write failed: {ex.GetType().Name} {ex.HResult}");
                return;
            }
        }
    }

    /// The temp file must sit in the SAME directory as the target.
    private void WriteAtomically(string json)
    {
        string temp = Path.Combine(_directory, TempFileName);
        string target = Path.Combine(_directory, FileName);

        // FileMode.Create silently overwrites an orphaned .tmp from an earlier run.
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Utf8NoBom.GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);                 // calls FlushFileBuffers
        }

        // File.Replace throws FileNotFoundException when the target does not exist yet.
        if (File.Exists(target)) File.Replace(temp, target, destinationBackupFileName: null);
        else File.Move(temp, target);
    }

    private static string Serialize(AppConfig config)
    {
        var dto = new ConfigFileWriteDto
        {
            SchemaVersion = config.SchemaVersion,
            Machine = new MachineWriteDto
            {
                CpuName = config.Machine.CpuName,
                LogicalProcessors = config.Machine.LogicalProcessors,
            },
            Settings = new SettingsWriteDto
            {
                PollIntervalMs = config.Settings.PollIntervalMs,
                StartWithWindows = config.Settings.StartWithWindows,
                WindowBounds = ToJson(config.Settings.WindowBounds),
                LogLevel = config.Settings.LogLevel.ToString().ToLowerInvariant(),
            },
            Rules = [.. config.Rules.Rules.Select(r => new RuleWriteDto
            {
                Id = r.Id.ToString(),
                ExeName = r.ExeName,
                LastKnownPath = r.LastKnownPath,
                Threads = [.. r.Threads.ToThreads()],
                Enabled = r.Enabled,
            })],
        };
        // Trailing "\n": a file without a final line ending is the exception everywhere.
        return JsonSerializer.Serialize(dto, WriteContext.ConfigFileWriteDto) + "\n";
    }

    private static WindowBoundsJson? ToJson(WindowBounds? bounds)
        => bounds is null ? null : new WindowBoundsJson { X = bounds.X, Y = bounds.Y, W = bounds.W, H = bounds.H };

    /// Caught inside Load() and turned into ConfigLoadOutcome.Corrupt; never leaves here.
    private sealed class ConfigFormatException(string field) : Exception(field);
}
