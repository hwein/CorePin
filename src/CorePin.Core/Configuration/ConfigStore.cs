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

/// Loads and writes %LOCALAPPDATA%\CorePin\config.json (S05). Load() never throws (§6.5),
/// Save() never throws and is neither debounced nor asynchronous (§5, §10.1).
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

    /// 02 §6 sells hand-correctability as an advantage, and a lastKnownPath with umlauts is
    /// barely readable for a human once every one of them is an escape sequence; TopologyJson
    /// uses the same encoder for the same reason. [JsonSourceGenerationOptions] has no Encoder
    /// property, so the option is carried in a second instance of the SAME generated context —
    /// source generation, start-up time and trimmability (D1) stay intact (MEASURED: the
    /// JsonSerializerOptions overload of Serialize warns IL2026/IL3050 instead).
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

    /// Parameterless. The logicalProcessors comparison and the setting of Rule.NeedsReview
    /// live in Program.Main, step 4b (S01 §3.7), NOT here. Never throws (S05 §6.5).
    public ConfigLoadResult Load()
    {
        if (!TryEnsureDirectory())
        {
            _log.Warn("config", "cannot create config directory, starting with in-memory defaults");   // config.dir-unavailable
            return Defaults(ConfigLoadOutcome.Missing);
        }

        string path = Path.Combine(_directory, FileName);
        if (!File.Exists(path))
        {
            _log.Info("config", "no config.json found, starting with defaults");                       // config.missing
            return Defaults(ConfigLoadOutcome.Missing);
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            // The file is there but could not be opened. It stays untouched — a Missing with
            // a writable session would overwrite the real rules once the lock falls (§6.4).
            _log.Warn("config",                                                                        // config.unreadable
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
            // The file is not touched at all (§7.1) — criterion 15 checks its timestamp.
            _log.Warn("config",                                                                        // config.too-new
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
            Rules = RuleSet.Empty,          // always empty, structurally (§8.3)
        };

        _log.Info("config", $"config.json loaded, {rules.Rules.Count} rules, schemaVersion {schemaVersion}");   // config.loaded
        return new ConfigLoadResult(config, rules, ConfigLoadOutcome.Loaded, detail, skippedRaw.Count);
    }

    /// Writes immediately and synchronously, atomically per 02 §12.2. NOT debounced.
    /// Ineffective (with config.save-discarded) while BlockWrites is set.
    /// PRECONDITION (§8.3): config.Rules must be built from the marked rule set.
    public void Save(AppConfig config)
    {
        if (!_guard.CanPersist)
        {
            _log.Warn("config", $"save discarded: WriteGuard.{_guard.Reason} active");                 // config.save-discarded
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
        // Idempotent, and called from Load() as well as Save() — AppPaths creates nothing
        // (S05 §2.2). Any failure is a state, not an exception, for both callers.
        try { Directory.CreateDirectory(_directory); return true; }
        catch (Exception) { return false; }
    }

    private ConfigLoadResult Corrupt(string path)
    {
        string name = FreeCorruptName();
        try { File.Move(path, Path.Combine(_directory, name)); }
        catch (Exception)
        {
            // Renaming needs write access of its own; if it fails the original file stays
            // under its old name and the outcome is still Corrupt (§6.2/§6.5).
        }

        _log.Warn("config", $"config.json unreadable, renamed to {name}, starting with defaults");     // config.corrupt
        return new ConfigLoadResult(AppConfig.Empty(), RuleSet.Empty, ConfigLoadOutcome.Corrupt, name, 0);
    }

    private string FreeCorruptName()
    {
        // Local time: a user looking at the file places it faster (D10).
        string stamp = _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string candidate = $"config.corrupt-{stamp}.json";
        for (int suffix = 2; File.Exists(Path.Combine(_directory, candidate)); suffix++)
            candidate = $"config.corrupt-{stamp}-{suffix}.json";
        return candidate;
    }

    private enum RawIntKind { Missing, Value, WrongType }

    /// A missing key (Undefined) is NOT a type error — a hand-corrected file that carries
    /// only settings.logLevel is the normal case of S02 §8.1, not corruption (§4.2).
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

        // cpuName is never checked, only displayed (02 §6).
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
        _log.Warn("config", $"pollIntervalMs {value} out of range, clamped to {clamped}");             // config.poll-interval-clamped
        return clamped;
    }

    private LogLevel ReadLogLevel(string? value)
    {
        if (value is null) return LogLevel.Info;
        if (string.Equals(value, "debug", StringComparison.OrdinalIgnoreCase)) return LogLevel.Debug;
        if (string.Equals(value, "info", StringComparison.OrdinalIgnoreCase)) return LogLevel.Info;
        if (string.Equals(value, "warn", StringComparison.OrdinalIgnoreCase)) return LogLevel.Warn;

        _log.Warn("config",                                                                            // config.log-level-invalid
            $"settings.logLevel '{value}' is not a valid level (debug|info|warn), using info");
        return LogLevel.Info;
    }

    private WindowBounds? ReadWindowBounds(WindowBoundsJson? bounds)
    {
        if (bounds is null) return null;

        // Discarded as a whole, not clamped field by field (§3.4).
        if (bounds.W < MinWindowWidth || bounds.H < MinWindowHeight)
        {
            _log.Warn("config", $"windowBounds {bounds.W}x{bounds.H} below minimum size, discarded");  // config.window-bounds-discarded
            return null;
        }
        return new WindowBounds(bounds.X, bounds.Y, bounds.W, bounds.H);
    }

    /// Top level and `settings` only — inside a rule a future additive field would warn on
    /// every load of an old rule although nothing is wrong (§4.3).
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
            _log.Warn("config", $"unknown field(s) ignored: {string.Join(", ", names)}");              // config.unknown-fields
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
                _log.Warn("config", $"rule at index {i} skipped: malformed entry");                    // config.rule-skipped
                skippedRaw.Add(element);
                continue;
            }

            if (!TryValidate(dto, out var rule, out string reason))
            {
                _log.Warn("config", $"rule at index {i} skipped: {reason}");                           // config.rule-skipped
                skippedRaw.Add(element);
                continue;
            }

            // File order decides: on a duplicate id or exeName the FIRST rule wins (§3.5).
            if (idToExeName.ContainsKey(rule.Id))
            {
                _log.Warn("config", $"rule '{rule.ExeName}' skipped: duplicate id {rule.Id}");         // config.rule-duplicate-id
                skippedRaw.Add(element);
                continue;
            }
            if (exeNameToFirstId.TryGetValue(rule.ExeName, out var firstId))
            {
                _log.Warn("config",                                                                    // config.rule-duplicate-exename
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
        // S02 §6 lists four reasons for config.rule-skipped and none of them fits a missing
        // or empty exeName; `malformed entry` is the only generic one of the four.
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
                _log.Warn("config", $"rule '{exeName}': thread index {thread} out of range, dropped"); // config.thread-index-dropped
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

    /// The only place where ConfigStore deletes a file of its own accord, and it only ever
    /// touches this one diagnostic file (§6.3). Guard-free like the rename (§6.6).
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
                    _log.Debug("config", "config.skipped.json removed, no rules skipped on this load");// config.skipped-file-removed
                }
            }
            catch (Exception)
            {
                // Best effort (AN-S05-4): a leftover diagnostic file is not worth a failure.
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
            // RulesSkipped and Outcome stay correct, only the diagnostic file is missing (§6.5).
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
            // A5: serialization sits INSIDE the try — Save() must never throw (§5.4/§10.1),
            // not even on a failure that happens before any file access. Not retryable
            // (no transient I/O state), so no retry attempt.
            _log.Warn("config", $"write failed: {ex.GetType().Name} {ex.HResult}");                    // config.write-failed
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
                _log.Warn("config",                                                                    // config.write-retry
                    $"write attempt {attempt + 1} failed: {ex.GetType().Name} {ex.HResult}, retrying");
                Thread.Sleep(backoffMs[attempt]);
            }
            catch (Exception ex)
            {
                // Every exception, like TryEnsureDirectory in Load(): Directory.CreateDirectory
                // also throws ArgumentException/NotSupportedException on a degenerate path, and
                // §5.4/§10.1 promise without qualification that Save never throws.
                // No ex.Message: it usually carries the full path and with it the Windows
                // user name (S02 §7.1, D15). UnauthorizedAccessException is never retried —
                // a permission problem does not resolve within milliseconds.
                _log.Warn("config", $"write failed: {ex.GetType().Name} {ex.HResult}");                // config.write-failed
                return;
            }
        }
    }

    /// 02 §12.2: temp file in the SAME directory, flush to disk, then replace.
    private void WriteAtomically(string json)
    {
        string temp = Path.Combine(_directory, TempFileName);
        string target = Path.Combine(_directory, FileName);

        // FileMode.Create silently overwrites an orphaned .tmp from an earlier run (§5.2).
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
        // Trailing "\n" like topology.json (S04 §3.4): a file without a final line ending is
        // the exception in every editor and every diff, and §9 sells hand-correctability.
        return JsonSerializer.Serialize(dto, WriteContext.ConfigFileWriteDto) + "\n";
    }

    private static WindowBoundsJson? ToJson(WindowBounds? bounds)
        => bounds is null ? null : new WindowBoundsJson { X = bounds.X, Y = bounds.Y, W = bounds.W, H = bounds.H };

    /// A type error on a mandatory field. Caught inside Load() and turned into
    /// ConfigLoadOutcome.Corrupt — it never leaves this class.
    private sealed class ConfigFormatException(string field) : Exception(field);
}
