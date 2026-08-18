using System.Diagnostics;
using System.Globalization;
using CorePin.Core.Diagnostics;
using CorePin.Core.Rules;
using CorePin.Core.Time;

namespace CorePin.Core.Configuration;

/// Loads and writes config.json. Load() and Save() never throw; Save is not debounced.
public sealed class ConfigStore
{
    private const int MaxSchemaVersion = 1;

    private readonly string _directory;
    private readonly ILog _log;
    private readonly ConfigReader _reader;
    private readonly RuleReader _ruleReader;
    private readonly ConfigSideFiles _sideFiles;
    private readonly ConfigFileWriter _writer;

    private WriteGuard _guard = WriteGuard.Open;

    public ConfigStore(string directory, ILog log, IClock clock)
    {
        _directory = directory;
        _log = log;
        _reader = new ConfigReader(log);
        _ruleReader = new RuleReader(log);
        _sideFiles = new ConfigSideFiles(directory, log, clock);
        _writer = new ConfigFileWriter(directory, log);
    }

    /// The logicalProcessors comparison and Rule.NeedsReview belong to the composition root.
    public ConfigLoadResult Load()
    {
        if (!TryEnsureDirectory())
        {
            _log.Error("config", "cannot create config directory, starting with in-memory defaults");
            return Defaults(ConfigLoadOutcome.Missing);
        }

        string path = Path.Combine(_directory, ConfigFileNames.Config);
        if (!File.Exists(path))
        {
            _log.Information("config", "no config.json found, starting with defaults");
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
            _log.Warning("config",
                $"config.json exists but could not be opened ({ex.GetType().Name}), starting with defaults; changes will not be saved");
            return Defaults(ConfigLoadOutcome.Unreadable);
        }

        if (!_reader.TryRead(text, out var header)) return Corrupt(path);

        if (header.SchemaVersion > MaxSchemaVersion)
        {
            // The file is not touched at all — its timestamp must stay unchanged.
            _log.Warning("config",
                $"config.json schemaVersion {header.SchemaVersion} is newer than supported ({MaxSchemaVersion}), UI is read-only");
            return new ConfigLoadResult(AppConfig.Empty(), RuleSet.Empty, ConfigLoadOutcome.TooNew,
                                        header.SchemaVersion.ToString(CultureInfo.InvariantCulture), 0);
        }

        _reader.ReportUnknownFields(text);

        var (rules, skippedRaw) = _ruleReader.Parse(header.Rules);
        string? detail = _sideFiles.UpdateSkipped(skippedRaw);

        var config = new AppConfig
        {
            SchemaVersion = header.SchemaVersion,
            Machine = header.Machine,
            Settings = header.Settings,
            Rules = RuleSet.Empty,          // always empty, structurally
        };

        _log.Information("config", $"config.json loaded, {rules.Rules.Count} rules, schemaVersion {header.SchemaVersion}");
        return new ConfigLoadResult(config, rules, ConfigLoadOutcome.Loaded, detail, skippedRaw.Count);
    }

    /// PRECONDITION: config.Rules must come from the marked rule set. No-op while blocked.
    public void Save(AppConfig config)
    {
        if (!_guard.CanPersist)
        {
            _log.Warning("config", $"save discarded: WriteGuard.{_guard.Reason} active");
            return;
        }
        _writer.Write(config);
    }

    public void BlockWrites(GuardReason reason) => _guard = reason switch
    {
        GuardReason.ConfigTooNew => WriteGuard.ReadOnly,
        GuardReason.ConfigUnreadable => WriteGuard.Unreadable,
        GuardReason.DebugTopology => WriteGuard.NoPersist,
        GuardReason.None => WriteGuard.Open,
        _ => throw new UnreachableException(),
    };

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
        string name = _sideFiles.RenameCorrupt(path);
        _log.Warning("config", $"config.json unreadable, renamed to {name}, starting with defaults");
        return new ConfigLoadResult(AppConfig.Empty(), RuleSet.Empty, ConfigLoadOutcome.Corrupt, name, 0);
    }
}
