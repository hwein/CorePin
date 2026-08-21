using System.Globalization;
using System.Text;
using CorePin.Core.Configuration;
using CorePin.Core.Diagnostics;
using CorePin.Core.Primitives;
using CorePin.Core.Rules;
using CorePin.Tests.Fakes;

namespace CorePin.Tests;

/// Runs against a real temporary directory (TempDir) — there is no IFileSystem abstraction.
public static class ConfigStoreTests
{
    private const string ConfigFile = ConfigFileNames.Config;
    private const string SkippedFile = ConfigFileNames.Skipped;
    private const string IdA = "8f3c1a2e-4b5d-4e6f-9a1b-2c3d4e5f6789";
    private const string IdB = "11111111-2222-3333-4444-555555555555";

    public static void Test_RoundTrip_AllFieldsSet()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        var store = NewStore(dir, log);

        var written = new AppConfig
        {
            SchemaVersion = 1,
            Machine = new MachineInfo("AMD Ryzen 9 7945HX with Radeon Graphics", 32),
            Settings = new Settings
            {
                PollIntervalMs = 1500,
                StartWithWindows = "admin",
                LogLevel = LogLevel.Warning,
                WindowBounds = new WindowBounds(100, 120, 660, 480),
            },
            Rules = new RuleSet(
            [
                new Rule
                {
                    Id = Guid.Parse(IdA),
                    ExeName = "cyberpunk2077.exe",
                    LastKnownPath = @"D:\Games\Cyberpunk 2077\bin\x64\cyberpunk2077.exe",
                    Threads = AffinityMask.FromThreads([0, 1, 2, 3]),
                    Enabled = true,
                },
                new Rule
                {
                    Id = Guid.Parse(IdB),
                    ExeName = "notepad.exe",
                    Threads = AffinityMask.FromThreads([16, 17]),
                    Enabled = false,
                },
            ]),
        };

        store.Save(written);
        var loaded = NewStore(dir, new RecordingLog()).Load();

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "a file just written must load");
        Assert.Equal(written.SchemaVersion, loaded.Config.SchemaVersion, "schemaVersion survives the round trip");
        Assert.Equal(written.Machine, loaded.Config.Machine, "machine survives the round trip");
        Assert.Equal(written.Settings, loaded.Config.Settings, "settings survive the round trip");
        Assert.Equal(2, loaded.RawRules.Rules.Count, "both rules survive the round trip");
        Assert.Equal(written.Rules.Rules[0], loaded.RawRules.Rules[0], "rule 1 survives field by field");
        Assert.Equal(written.Rules.Rules[1], loaded.RawRules.Rules[1], "rule 2 survives field by field");
    }

    public static void Test_RoundTrip_EmptyRuleList()
    {
        using var dir = new TempDir();
        var store = NewStore(dir, new RecordingLog());

        store.Save(SampleConfig());
        var loaded = NewStore(dir, new RecordingLog()).Load();

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "an empty rule list is a valid file");
        Assert.Equal(0, loaded.RawRules.Rules.Count, "RuleSet.Empty survives the round trip");
    }

    public static void Test_Defaults_MissingSettings()
    {
        var (loaded, _) = LoadJson("""{ "schemaVersion": 1, "machine": { "cpuName": "X", "logicalProcessors": 8 } }""");

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "a missing settings object is no corruption");
        Assert.Equal(new Settings(), loaded.Config.Settings, "every settings field falls back to its default");
    }

    public static void Test_Defaults_MissingMachine()
    {
        var (loaded, _) = LoadJson("""{ "schemaVersion": 1 }""");

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "a missing machine object is no corruption");
        Assert.Equal(new MachineInfo("", 0), loaded.Config.Machine, "cpuName defaults to empty, logicalProcessors to 0");
    }

    /// A typical hand-corrected file — no schemaVersion, no machine, just settings.
    public static void Test_Defaults_PartialSettings()
    {
        var (loaded, log) = LoadJson("""{ "settings": { "logLevel": "debug" } }""");

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "the hand-corrected minimal file must load");
        Assert.Equal(1, loaded.Config.SchemaVersion, "a missing schemaVersion defaults to 1");
        Assert.Equal(LogLevel.Debug, loaded.Config.Settings.LogLevel, "logLevel is read");
        Assert.Equal(1000, loaded.Config.Settings.PollIntervalMs, "the remaining settings fields stay at their default");
        Assert.Equal("off", loaded.Config.Settings.StartWithWindows, "startWithWindows stays at its default");
        Assert.True(loaded.Config.Settings.WindowBounds is null, "windowBounds stays unset");
        Assert.Equal(0, CorruptFiles(log), "no rename happened");
    }

    public static void Test_Defaults_PartialMachine()
    {
        var (loaded, _) = LoadJson("""{ "schemaVersion": 1, "machine": { "cpuName": "X" } }""");

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "a partially filled machine object is no corruption");
        Assert.Equal(new MachineInfo("X", 0), loaded.Config.Machine, "a missing logicalProcessors defaults to 0");
    }

    public static void Test_SchemaVersion_Missing_DefaultsToOne_NoRename()
    {
        var (loaded, log) = LoadJson("""{ "machine": { "logicalProcessors": 8 } }""");

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "a missing schemaVersion key is not corruption");
        Assert.Equal(1, loaded.Config.SchemaVersion, "the default is 1");
        Assert.Equal(0, CorruptFiles(log), "no rename happened");
    }

    public static void Test_SchemaVersion_BelowOne_TreatedAsMissing()
    {
        foreach (string value in new[] { "0", "-1" })
        {
            var (loaded, _) = LoadJson($$"""{ "schemaVersion": {{value}} }""");

            Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, $"schemaVersion {value} is not corruption");
            Assert.Equal(1, loaded.Config.SchemaVersion, $"schemaVersion {value} normalises to 1");
        }
    }

    public static void Test_SchemaVersion_TypeError_MakesFileUnreadable()
    {
        var (loaded, log) = LoadJson("""{ "schemaVersion": "eins" }""");

        Assert.Equal(ConfigLoadOutcome.Corrupt, loaded.Outcome, "a wrong JSON type on schemaVersion makes the file unreadable");
        Assert.True(log.Has("config.json unreadable, renamed to config.corrupt-"), "config.corrupt is logged");
    }

    public static void Test_PollIntervalMs_OutOfRange_IsClamped()
    {
        var (loaded, log) = LoadJson("""{ "settings": { "pollIntervalMs": 99999 } }""");

        Assert.Equal(5000, loaded.Config.Settings.PollIntervalMs, "99999 clamps to the upper bound");
        Assert.True(log.Has("pollIntervalMs 99999 out of range, clamped to 5000"), "config.poll-interval-clamped is logged");
    }

    public static void Test_PollIntervalMs_TypeError_MakesFileUnreadable()
    {
        var (loaded, _) = LoadJson("""{ "settings": { "pollIntervalMs": "schnell" } }""");

        Assert.Equal(ConfigLoadOutcome.Corrupt, loaded.Outcome, "a wrong JSON type on pollIntervalMs makes the file unreadable");
    }

    public static void Test_PollIntervalMs_Missing_IsNoTypeError()
    {
        var (loaded, _) = LoadJson("""{ "settings": { "startWithWindows": "off" } }""");

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "an absent key is no type error");
        Assert.Equal(1000, loaded.Config.Settings.PollIntervalMs, "the default applies");
    }

    public static void Test_LogicalProcessors_OutOfRange_IsNormalized()
    {
        foreach (string value in new[] { "-5", "999" })
        {
            var (loaded, _) = LoadJson($$"""{ "machine": { "logicalProcessors": {{value}} } }""");

            Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, $"logicalProcessors {value} is no corruption");
            Assert.Equal(0, loaded.Config.Machine.LogicalProcessors, $"logicalProcessors {value} normalises to 0");
        }
    }

    public static void Test_LogicalProcessors_TypeError_MakesFileUnreadable()
    {
        var (loaded, _) = LoadJson("""{ "machine": { "logicalProcessors": "zweiunddreissig" } }""");

        Assert.Equal(ConfigLoadOutcome.Corrupt, loaded.Outcome, "a wrong JSON type on logicalProcessors makes the file unreadable");
    }

    public static void Test_LogLevel_AllSpellingsParsed()
    {
        var cases = new (string Value, LogLevel Expected)[]
        {
            ("trace", LogLevel.Trace),
            ("debug", LogLevel.Debug),
            ("information", LogLevel.Information),
            ("warning", LogLevel.Warning),
            ("error", LogLevel.Error),
            ("critical", LogLevel.Critical),
            ("info", LogLevel.Information),
            ("warn", LogLevel.Warning),
        };

        foreach (var (value, expected) in cases)
        {
            var (loaded, _) = LoadJson($$"""{ "settings": { "logLevel": "{{value}}" } }""");
            Assert.Equal(expected, loaded.Config.Settings.LogLevel, $"logLevel '{value}' parses to {expected}");
        }
    }

    public static void Test_LogLevel_UnknownString_FallsBackToInformation()
    {
        var (loaded, log) = LoadJson("""{ "settings": { "logLevel": "verbose" } }""");

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "an unknown level is no reason for corruption");
        Assert.Equal(LogLevel.Information, loaded.Config.Settings.LogLevel, "the fallback is information");
        Assert.True(log.Has("settings.logLevel 'verbose' is not a valid level "
            + "(trace|debug|information|warning|error|critical), using information"),
            "config.log-level-invalid is logged");
    }

    public static void Test_WindowBounds_TooSmall()
    {
        var (loaded, log) = LoadJson("""{ "settings": { "windowBounds": { "x": 10, "y": 10, "w": 100, "h": 100 } } }""");

        Assert.True(loaded.Config.Settings.WindowBounds is null, "the whole object is discarded, not clamped");
        Assert.True(log.Has("windowBounds 100x100 below minimum size, discarded"),
            "config.window-bounds-discarded is logged");
    }

    public static void Test_Rule_MissingId_SkipsRuleNotFile()
    {
        var (loaded, log) = LoadJson($$"""
        {
          "rules": [
            { "exeName": "a.exe", "threads": [0] },
            { "id": "{{IdA}}", "exeName": "b.exe", "threads": [1] }
          ]
        }
        """);

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "one broken rule does not condemn the file");
        Assert.Equal(1, loaded.RawRules.Rules.Count, "only the valid rule survives");
        Assert.Equal("b.exe", loaded.RawRules.Rules[0].ExeName, "the valid rule is the second one");
        Assert.Equal(1, loaded.RulesSkipped, "one rule was skipped");
        Assert.True(log.Has("rule at index 0 skipped: missing/invalid id"), "config.rule-skipped carries the index");
    }

    public static void Test_Rule_MissingOrEmptyExeName_IsSkipped()
    {
        var (loaded, log) = LoadJson($$"""
        {
          "rules": [
            { "id": "{{IdA}}", "threads": [0] },
            { "id": "{{IdB}}", "exeName": "  ", "threads": [1] }
          ]
        }
        """);

        Assert.Equal(0, loaded.RawRules.Rules.Count, "both a missing and a blank exeName drop the rule");
        Assert.Equal(2, loaded.RulesSkipped, "both rules were skipped");
        Assert.True(log.Has("rule at index 0 skipped: missing exeName"), "a missing exeName key is reported by name");
        Assert.True(log.Has("rule at index 1 skipped: missing exeName"), "a whitespace-only exeName trims to empty, same reason");
    }

    public static void Test_Rule_EmptyThreads_IsSkipped()
    {
        var (loaded, log) = LoadJson($$"""{ "rules": [ { "id": "{{IdA}}", "exeName": "a.exe", "threads": [] } ] }""");

        Assert.Equal(0, loaded.RawRules.Rules.Count, "a rule without threads is dropped");
        Assert.Equal(1, loaded.RulesSkipped, "the rule counts as skipped");
        Assert.True(log.Has("rule at index 0 skipped: empty threads"), "config.rule-skipped names the reason");
    }

    public static void Test_Rule_ThreadIndexAbove63_IsFilteredNotTheRule()
    {
        var (loaded, log) = LoadJson($$"""{ "rules": [ { "id": "{{IdA}}", "exeName": "a.exe", "threads": [5, 64] } ] }""");

        Assert.Equal(1, loaded.RawRules.Rules.Count, "the rule itself survives");
        Assert.Equal(AffinityMask.FromThreads([5]), loaded.RawRules.Rules[0].Threads, "only thread 5 remains");
        Assert.True(log.Has("rule 'a.exe': thread index 64 out of range, dropped"),
            "config.thread-index-dropped is logged");
    }

    /// A plain `bool?` binding could never report this reason: a non-boolean token
    /// throws JsonException there, and null silently becomes true (measured).
    public static void Test_Rule_NonBooleanEnabled_IsSkippedWithItsOwnReason()
    {
        foreach (string value in new[] { "\"yes\"", "1", "null" })
        {
            var (loaded, log) = LoadJson($$"""
            { "rules": [ { "id": "{{IdA}}", "exeName": "a.exe", "threads": [0], "enabled": {{value}} } ] }
            """);

            Assert.Equal(0, loaded.RawRules.Rules.Count, $"enabled {value} skips the rule");
            Assert.True(log.Has("rule at index 0 skipped: invalid enabled"),
                $"enabled {value} is reported as `invalid enabled`, not as `malformed entry`");
        }
    }

    public static void Test_Rule_DuplicateThreads_AreCollapsed()
    {
        var (loaded, _) = LoadJson($$"""{ "rules": [ { "id": "{{IdA}}", "exeName": "a.exe", "threads": [3, 3, 4] } ] }""");

        Assert.Equal(AffinityMask.FromThreads([3, 4]), loaded.RawRules.Rules[0].Threads, "duplicates collapse into one mask");
    }

    public static void Test_Rule_DuplicateId_SecondIsSkipped()
    {
        var (loaded, log) = LoadJson($$"""
        {
          "rules": [
            { "id": "{{IdA}}", "exeName": "first.exe", "threads": [0] },
            { "id": "{{IdA}}", "exeName": "second.exe", "threads": [1] }
          ]
        }
        """);

        Assert.Equal(1, loaded.RawRules.Rules.Count, "the first rule wins");
        Assert.Equal("first.exe", loaded.RawRules.Rules[0].ExeName, "file order decides");
        Assert.True(log.Has($"rule 'second.exe' skipped: duplicate id {IdA}"),
            "config.rule-duplicate-id carries exeName and guid");
    }

    public static void Test_Rule_DuplicateExeName_SecondIsSkipped()
    {
        var (loaded, log) = LoadJson($$"""
        {
          "rules": [
            { "id": "{{IdA}}", "exeName": "game.exe", "threads": [0] },
            { "id": "{{IdB}}", "exeName": "GAME.EXE", "threads": [1] }
          ]
        }
        """);

        Assert.Equal(1, loaded.RawRules.Rules.Count, "exeName comparison is OrdinalIgnoreCase");
        Assert.True(log.Has($"rule 'GAME.EXE' skipped: duplicate exeName, rule {IdA} already covers this program"),
            "config.rule-duplicate-exename names the first id");
    }

    public static void Test_SkippedRules_CreateSkippedFile()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        Write(dir, $$"""{ "rules": [ { "exeName": "a.exe", "threads": [0] } ] }""");

        var loaded = NewStore(dir, log).Load();
        string skipped = Path.Combine(dir.Path, SkippedFile);

        Assert.True(File.Exists(skipped), "config.skipped.json is written");
        Assert.True(File.ReadAllText(skipped).Contains("a.exe", StringComparison.Ordinal),
            "it contains the raw skipped entry");
        Assert.Equal(SkippedFile, loaded.Detail, "Detail points at the skipped file");
    }

    public static void Test_NoSkippedRules_NoSkippedFile()
    {
        using var dir = new TempDir();
        Write(dir, $$"""{ "rules": [ { "id": "{{IdA}}", "exeName": "a.exe", "threads": [0] } ] }""");

        var loaded = NewStore(dir, new RecordingLog()).Load();

        Assert.True(!File.Exists(Path.Combine(dir.Path, SkippedFile)), "a clean file produces no skipped file");
        Assert.True(loaded.Detail is null, "Detail stays empty");
    }

    public static void Test_SkippedFile_IsDeletedOnNextCleanLoad()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();

        Write(dir, $$"""{ "rules": [ { "exeName": "a.exe", "threads": [0] } ] }""");
        NewStore(dir, log).Load();
        Assert.True(File.Exists(Path.Combine(dir.Path, SkippedFile)), "the skipped file exists after the broken load");

        Write(dir, $$"""{ "rules": [ { "id": "{{IdA}}", "exeName": "a.exe", "threads": [0] } ] }""");
        NewStore(dir, log).Load();

        Assert.True(!File.Exists(Path.Combine(dir.Path, SkippedFile)), "a clean load removes the stale file");
        Assert.True(log.Has("config.skipped.json removed, no rules skipped on this load"),
            "config.skipped-file-removed is logged");
    }

    public static void Test_SkippedFile_IsRemovedOnCorruptLoad()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();

        Write(dir, $$"""{ "rules": [ { "exeName": "a.exe", "threads": [0] } ] }""");
        NewStore(dir, log).Load();
        Assert.True(File.Exists(Path.Combine(dir.Path, SkippedFile)), "the skipped file exists after the broken-rule load");

        Write(dir, "{ broken");
        var loaded = NewStore(dir, log).Load();

        Assert.Equal(ConfigLoadOutcome.Corrupt, loaded.Outcome, "precondition: this load is corrupt");
        Assert.True(!File.Exists(Path.Combine(dir.Path, SkippedFile)), "a corrupt load also removes the stale skipped file");
        Assert.True(log.Has("config.skipped.json removed, no rules skipped on this load"),
            "config.skipped-file-removed fires on the Corrupt path too");
    }

    public static void Test_SkippedFile_IsOverwrittenNotAppended()
    {
        using var dir = new TempDir();

        Write(dir, """{ "rules": [ { "exeName": "first.exe", "threads": [0] } ] }""");
        NewStore(dir, new RecordingLog()).Load();

        Write(dir, """{ "rules": [ { "exeName": "second.exe", "threads": [0] } ] }""");
        NewStore(dir, new RecordingLog()).Load();

        string text = File.ReadAllText(Path.Combine(dir.Path, SkippedFile));
        Assert.True(text.Contains("second.exe", StringComparison.Ordinal), "the second run is in the file");
        Assert.True(!text.Contains("first.exe", StringComparison.Ordinal), "the first run is gone");
    }

    public static void Test_UnknownKeys_SingleWarnLine()
    {
        var (loaded, log) = LoadJson("""{ "pollInterval": 700, "settings": { "logevel": "debug" } }""");

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome, "an unknown key is no corruption");
        Assert.Equal(1000, loaded.Config.Settings.PollIntervalMs, "the misspelled field has no effect");
        Assert.Equal(1, log.Count("unknown field(s) ignored:"), "exactly one config.unknown-fields line");
        Assert.True(log.Has("unknown field(s) ignored: pollInterval, settings.logevel"),
            "both names appear in one line");
    }

    public static void Test_RuleJson_HasNoNeedsReviewField()
    {
        var (loaded, log) = LoadJson($$"""
        { "rules": [ { "id": "{{IdA}}", "exeName": "a.exe", "threads": [0], "needsReview": true } ] }
        """);

        Assert.Equal(1, loaded.RawRules.Rules.Count, "an unknown key inside a rule does not skip it");
        Assert.True(!loaded.RawRules.Rules[0].NeedsReview, "needsReview is never read from the file");
        Assert.Equal(0, log.Count("unknown field(s) ignored:"), "unknown keys inside rules are not reported");
    }

    public static void Test_AtomicWrite_FirstSave()
    {
        using var dir = new TempDir();
        NewStore(dir, new RecordingLog()).Save(SampleConfig());

        Assert.True(File.Exists(Path.Combine(dir.Path, ConfigFile)), "config.json exists after the first save");
        Assert.True(!File.Exists(Path.Combine(dir.Path, ConfigFileNames.Temp)), "the temp file is gone");
    }

    public static void Test_AtomicWrite_OverwritesExisting()
    {
        using var dir = new TempDir();
        var store = NewStore(dir, new RecordingLog());

        store.Save(SampleConfig(logicalProcessors: 8));
        store.Save(SampleConfig(logicalProcessors: 32));

        string text = File.ReadAllText(Path.Combine(dir.Path, ConfigFile));
        Assert.True(text.Contains("\"logicalProcessors\": 32", StringComparison.Ordinal), "the second save replaced the content");
        Assert.True(!text.Contains("\"logicalProcessors\": 8", StringComparison.Ordinal), "nothing of the first save remains");
    }

    public static void Test_AtomicWrite_OrphanedTempIsOverwritten()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, ConfigFileNames.Temp), "garbage left behind");

        NewStore(dir, new RecordingLog()).Save(SampleConfig());

        Assert.True(File.Exists(Path.Combine(dir.Path, ConfigFile)), "the save succeeded");
        Assert.True(!File.Exists(Path.Combine(dir.Path, ConfigFileNames.Temp)), "the orphan was overwritten and consumed");
    }

    public static void Test_AtomicWrite_TransientLockIsRetried()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        var store = NewStore(dir, log);
        store.Save(SampleConfig(logicalProcessors: 8));

        string target = Path.Combine(dir.Path, ConfigFile);
        using var acquired = new ManualResetEventSlim(false);
        var holder = new Thread(() =>
        {
            using var stream = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            acquired.Set();
            Thread.Sleep(120);
        })
        { IsBackground = true };
        holder.Start();
        acquired.Wait(2000);

        store.Save(SampleConfig(logicalProcessors: 32));
        holder.Join(2000);

        Assert.True(log.Has("write attempt 1 failed:"), "at least one config.write-retry line");
        Assert.True(!log.Joined.Contains(dir.Path, StringComparison.OrdinalIgnoreCase), "the retry line carries no path");
        Assert.True(File.ReadAllText(target).Contains("\"logicalProcessors\": 32", StringComparison.Ordinal),
            "the save succeeded after the retry");
    }

    public static void Test_AtomicWrite_PermanentFailure_DoesNotThrow()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        // A DIRECTORY where the temp file has to go: permanently not writable, and
        // UnauthorizedAccessException is deliberately not retried.
        Directory.CreateDirectory(Path.Combine(dir.Path, ConfigFileNames.Temp));

        NewStore(dir, log).Save(SampleConfig());

        Assert.True(log.Has("write failed: UnauthorizedAccessException "), "config.write-failed names type and HResult");
        Assert.True(!log.Joined.Contains(dir.Path, StringComparison.OrdinalIgnoreCase), "no path, no ex.Message");
        Assert.True(!File.Exists(Path.Combine(dir.Path, ConfigFile)), "nothing was written");
    }

    public static void Test_Save_WritesExactlyTheGivenMachineValue()
    {
        using var dir = new TempDir();
        var store = NewStore(dir, new RecordingLog());
        string target = Path.Combine(dir.Path, ConfigFile);

        store.Save(SampleConfig(logicalProcessors: 8));
        Assert.True(File.ReadAllText(target).Contains("\"logicalProcessors\": 8", StringComparison.Ordinal),
            "Save writes exactly the given value");

        store.Save(SampleConfig(logicalProcessors: 16));
        Assert.True(File.ReadAllText(target).Contains("\"logicalProcessors\": 16", StringComparison.Ordinal),
            "Save never computes or compares the value itself");
    }

    public static void Test_Save_WritesInfoNotInformationForLogLevel()
    {
        using var dir = new TempDir();
        var store = NewStore(dir, new RecordingLog());

        store.Save(SampleConfig());

        string text = File.ReadAllText(Path.Combine(dir.Path, ConfigFile));
        Assert.True(text.Contains("\"logLevel\": \"info\"", StringComparison.Ordinal),
            "the written vocabulary is info, not information");
    }

    /// LF, no BOM, two-space indent — measured, not assumed.
    public static void Test_Save_ProducesLfWithoutBomAndTwoSpaceIndent()
    {
        using var dir = new TempDir();
        NewStore(dir, new RecordingLog()).Save(SampleConfig(logicalProcessors: 32));

        byte[] bytes = File.ReadAllBytes(Path.Combine(dir.Path, ConfigFile));
        Assert.True(bytes.Length < 3 || !(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "no UTF-8 BOM");
        Assert.Equal(0, bytes.Count(b => b == 0x0D), "no CR — NewLine is \"\\n\"");

        string text = new UTF8Encoding(false).GetString(bytes);
        Assert.Equal(
            "{\n"
            + "  \"schemaVersion\": 1,\n"
            + "  \"machine\": {\n"
            + "    \"cpuName\": \"Test CPU\",\n"
            + "    \"logicalProcessors\": 32\n"
            + "  },\n"
            + "  \"settings\": {\n"
            + "    \"pollIntervalMs\": 1000,\n"
            + "    \"startWithWindows\": \"off\",\n"
            + "    \"windowBounds\": null,\n"
            + "    \"logLevel\": \"info\"\n"
            + "  },\n"
            + "  \"rules\": []\n"
            + "}\n",
            text,
            "field order and indentation are fixed, one trailing newline like topology.json");
        Assert.True(!text.EndsWith("}\n\n", StringComparison.Ordinal), "exactly one trailing newline, not two");
    }

    /// A name or path is only hand-correctable when it stands in the file as itself
    /// and not as an escape sequence.
    public static void Test_Save_LeavesNonAsciiAndHtmlCharactersUnescaped()
    {
        using var dir = new TempDir();
        const string CpuName = "AMD Ryzen 9 <Größe> & Co +1";
        const string LastKnownPath = @"D:\Spiele\Grüße & Söhne\x64\spiel+1.exe";

        var written = new AppConfig
        {
            SchemaVersion = 1,
            Machine = new MachineInfo(CpuName, 32),
            Settings = new Settings(),
            Rules = new RuleSet(
            [
                new Rule
                {
                    Id = Guid.Parse(IdA),
                    ExeName = "spiel+1.exe",
                    LastKnownPath = LastKnownPath,
                    Threads = AffinityMask.FromThreads([0]),
                    Enabled = true,
                },
            ]),
        };

        NewStore(dir, new RecordingLog()).Save(written);

        byte[] bytes = File.ReadAllBytes(Path.Combine(dir.Path, ConfigFile));
        Assert.True(bytes.Length < 3 || !(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "still no UTF-8 BOM");
        Assert.Equal(0, bytes.Count(b => b == 0x0D), "still no CR");

        string text = new UTF8Encoding(false).GetString(bytes);
        Assert.True(text.Contains($"\"cpuName\": \"{CpuName}\"", StringComparison.Ordinal),
            $"umlaut, ampersand, angle bracket and plus stand unescaped in cpuName; file was:\n{text}");
        Assert.True(text.Contains("Grüße & Söhne", StringComparison.Ordinal), "and unescaped in lastKnownPath");
        Assert.True(!text.Contains("\\u", StringComparison.Ordinal), "no \\uXXXX escape anywhere in the file");

        var loaded = NewStore(dir, new RecordingLog()).Load();
        Assert.Equal(CpuName, loaded.Config.Machine.CpuName, "the round trip stays lossless");
        Assert.Equal(LastKnownPath, loaded.RawRules.Rules[0].LastKnownPath, "the path survives the round trip");
    }

    /// Save promises without qualification to return instead of throwing.
    /// Directory.CreateDirectory throws ArgumentException on these strings, which is neither
    /// IOException nor UnauthorizedAccessException.
    public static void Test_Save_DegenerateDirectory_DoesNotThrow()
    {
        foreach (string directory in new[] { "", "   ", "\0bad" })
        {
            var log = new RecordingLog();
            new ConfigStore(directory, log, new FakeClock()).Save(SampleConfig());

            Assert.True(log.Has("write failed: "),
                $"directory '{directory.Replace("\0", "\\0", StringComparison.Ordinal)}' is reported "
                + "as config.write-failed instead of throwing");
        }
    }

    public static void Test_Rename_InvalidJson()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        Write(dir, """{ "schemaVersion": 1, """);

        var loaded = NewStore(dir, log).Load();

        Assert.Equal(ConfigLoadOutcome.Corrupt, loaded.Outcome, "a syntax error makes the file unreadable");
        Assert.Equal(1, CorruptFiles(dir), "exactly one renamed file");
        Assert.True(!File.Exists(Path.Combine(dir.Path, ConfigFile)), "config.json is gone from its old name");
        Assert.True(log.Has("config.json unreadable, renamed to config.corrupt-"), "config.corrupt is logged");
    }

    public static void Test_Rename_EmptyFile()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, ConfigFile), []);

        var loaded = NewStore(dir, new RecordingLog()).Load();

        Assert.Equal(ConfigLoadOutcome.Corrupt, loaded.Outcome, "a 0-byte file is treated like a syntax error");
        Assert.Equal(1, CorruptFiles(dir), "the empty file was renamed");
    }

    public static void Test_Rename_NameCollision()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();

        Write(dir, "{ broken");
        var first = new ConfigStore(dir.Path, new RecordingLog(), clock).Load();

        Write(dir, "{ broken again");
        var second = new ConfigStore(dir.Path, new RecordingLog(), clock).Load();

        Assert.Equal(2, CorruptFiles(dir), "two corruption events, two files");
        Assert.True(second.Detail is not null && second.Detail.EndsWith("-2.json", StringComparison.Ordinal),
            $"the second name carries the -2 suffix, was '{second.Detail}' after '{first.Detail}'");
    }

    public static void Test_Rename_Fails_LoadStillDoesNotThrow()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        var log = new RecordingLog();
        Write(dir, "{ broken");

        // A DIRECTORY under the name the rename is going to pick: File.Exists says no, so
        // the free-name search keeps it, and File.Move then fails.
        string stamp = clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        Directory.CreateDirectory(Path.Combine(dir.Path, $"config.corrupt-{stamp}.json"));

        var loaded = new ConfigStore(dir.Path, log, clock).Load();

        Assert.Equal(ConfigLoadOutcome.Corrupt, loaded.Outcome, "the outcome is Corrupt even when the rename fails");
        Assert.True(File.Exists(Path.Combine(dir.Path, ConfigFile)), "the original file stays under its old name");
        Assert.True(loaded.Detail is null, "Detail is null when the rename failed");
        Assert.True(log.Has("config.json unreadable, rename failed, starting with defaults"),
            "the failed rename gets its own log line, not the renamed-to one");
        Assert.True(!log.Has("config.skipped.json removed, no rules skipped on this load"),
            "no skipped file existed, so the cleanup call logs nothing");
    }

    public static void Test_Rename_Fails_SkippedFileIsStillRemoved()
    {
        using var dir = new TempDir();
        var clock = new FakeClock();
        var log = new RecordingLog();

        Write(dir, $$"""{ "rules": [ { "exeName": "a.exe", "threads": [0] } ] }""");
        new ConfigStore(dir.Path, log, clock).Load();
        Assert.True(File.Exists(Path.Combine(dir.Path, SkippedFile)), "the skipped file exists after the broken-rule load");

        Write(dir, "{ broken");

        // Same forced-failure scaffold as Test_Rename_Fails_LoadStillDoesNotThrow: a DIRECTORY
        // under the name the rename is going to pick makes File.Move fail.
        string stamp = clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        Directory.CreateDirectory(Path.Combine(dir.Path, $"config.corrupt-{stamp}.json"));

        var loaded = new ConfigStore(dir.Path, log, clock).Load();

        Assert.Equal(ConfigLoadOutcome.Corrupt, loaded.Outcome, "the outcome is Corrupt even when the rename fails");
        Assert.True(loaded.Detail is null, "Detail is still null when the rename fails");
        Assert.True(!File.Exists(Path.Combine(dir.Path, SkippedFile)),
            "the stale skipped file is removed even though the rename itself failed");
        Assert.True(log.Has("config.skipped.json removed, no rules skipped on this load"),
            "the cleanup runs and logs regardless of the rename outcome");
        Assert.True(log.Has("config.json unreadable, rename failed, starting with defaults"),
            "the failed rename still gets its own log line");
    }

    public static void Test_Unreadable_NoRenameNoWrite()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        Write(dir, """{ "schemaVersion": 1 }""");

        ConfigLoadResult loaded;
        using (new FileStream(Path.Combine(dir.Path, ConfigFile), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            loaded = NewStore(dir, log).Load();
        }

        Assert.Equal(ConfigLoadOutcome.Unreadable, loaded.Outcome, "a locked file is Unreadable, not Missing");
        Assert.True(log.Has("config.json exists but could not be opened ("), "config.unreadable is logged");
        Assert.Equal(0, CorruptFiles(dir), "nothing was renamed");
        Assert.Equal(new WriteGuard(false, false, GuardReason.ConfigUnreadable), WriteGuard.Unreadable,
            "the guard for this outcome is as restrictive as ReadOnly");
    }

    public static void Test_Missing_NoBlockJustLog()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        var store = NewStore(dir, log);

        var loaded = store.Load();
        Assert.Equal(ConfigLoadOutcome.Missing, loaded.Outcome, "no file means Missing");
        Assert.True(log.Has("no config.json found, starting with defaults"), "config.missing is logged");

        store.Save(SampleConfig());
        Assert.True(File.Exists(Path.Combine(dir.Path, ConfigFile)), "a following Save works normally");
    }

    public static void Test_TooNew_FileStaysUnchanged()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        Write(dir, $$"""{ "schemaVersion": 99, "rules": [ { "id": "{{IdA}}", "exeName": "a.exe", "threads": [0] } ] }""");

        string target = Path.Combine(dir.Path, ConfigFile);
        var before = File.GetLastWriteTimeUtc(target);
        var loaded = NewStore(dir, log).Load();
        var after = File.GetLastWriteTimeUtc(target);

        Assert.Equal(ConfigLoadOutcome.TooNew, loaded.Outcome, "schemaVersion 99 is too new");
        Assert.Equal(0, loaded.Config.Rules.Rules.Count, "Config.Rules is empty");
        Assert.Equal(0, loaded.RawRules.Rules.Count, "RawRules is empty too — the file is not even parsed for rules");
        Assert.Equal(before, after, "the file was not touched");
        Assert.True(log.Has("config.json schemaVersion 99 is newer than supported (1), UI is read-only"),
            "config.too-new is logged");
    }

    public static void Test_TooNew_SaveIsRefused()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        Write(dir, """{ "schemaVersion": 99 }""");

        var store = NewStore(dir, log);
        var loaded = store.Load();
        Assert.Equal(ConfigLoadOutcome.TooNew, loaded.Outcome, "precondition of this test");

        store.BlockWrites(GuardReason.ConfigTooNew);
        string before = File.ReadAllText(Path.Combine(dir.Path, ConfigFile));
        store.Save(SampleConfig(logicalProcessors: 32));

        Assert.Equal(before, File.ReadAllText(Path.Combine(dir.Path, ConfigFile)), "the file is unchanged");
        Assert.True(log.Has("save discarded: WriteGuard.ConfigTooNew active"), "config.save-discarded is logged");
    }

    public static void Test_NoPersist_RulesStayApplicable_SaveIsDiscarded()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        var store = NewStore(dir, log);

        store.BlockWrites(GuardReason.DebugTopology);
        store.Save(SampleConfig());

        Assert.True(!File.Exists(Path.Combine(dir.Path, ConfigFile)), "nothing is persisted");
        Assert.True(log.Has("save discarded: WriteGuard.DebugTopology active"), "config.save-discarded is logged");
        Assert.True(WriteGuard.NoPersist.CanEditRules, "editing and pinning stay allowed");
    }

    public static void Test_Load_ReturnsRawRulesSeparately()
    {
        var (loaded, _) = LoadJson($$"""
        {
          "rules": [
            { "id": "{{IdA}}", "exeName": "a.exe", "threads": [0] },
            { "id": "{{IdB}}", "exeName": "b.exe", "threads": [1] }
          ]
        }
        """);

        Assert.Equal(0, loaded.Config.Rules.Rules.Count, "Config.Rules is structurally always empty");
        Assert.Equal(2, loaded.RawRules.Rules.Count, "the real rules live in RawRules");
    }

    public static void Test_Load_SetsNeedsReviewNowhere()
    {
        var (loaded, _) = LoadJson($$"""
        {
          "machine": { "logicalProcessors": 4 },
          "rules": [
            { "id": "{{IdA}}", "exeName": "a.exe", "threads": [0, 1, 2, 3] },
            { "id": "{{IdB}}", "exeName": "b.exe", "threads": [40] }
          ]
        }
        """);

        Assert.Equal(2, loaded.RawRules.Rules.Count, "both rules load");
        Assert.True(loaded.RawRules.Rules.All(r => !r.NeedsReview),
            "ConfigStore never sets NeedsReview — that is the composition root's job");
    }

    public static void Test_Directory_NotCreatable_LoadReturnsMissing()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();

        var loaded = new ConfigStore(BlockedDirectory(dir), log, new FakeClock()).Load();

        Assert.Equal(ConfigLoadOutcome.Missing, loaded.Outcome, "an uncreatable directory yields Missing");
        Assert.True(log.Has("cannot create config directory, starting with in-memory defaults"),
            "config.dir-unavailable is logged");
    }

    public static void Test_Directory_NotCreatable_SaveFailsSilently()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();

        new ConfigStore(BlockedDirectory(dir), log, new FakeClock()).Save(SampleConfig());

        Assert.True(log.Has("write failed: IOException "), "config.write-failed names type and HResult");
        Assert.True(!log.Joined.Contains(dir.Path, StringComparison.OrdinalIgnoreCase), "no path in the line");
    }

    /// Every event wording is asserted above, no assertion above looks at the level.
    /// config.skipped-file-removed is the only debug event of the config category — raised
    /// to info it would stand in every shipped log.
    public static void Test_LogLevels_OfTheConfigEvents()
    {
        using var dir = new TempDir();
        var log = new RecordingLog();

        NewStore(dir, log).Load();
        Assert.True(log.Has(LogLevel.Information, "no config.json found, starting with defaults"),
            "config.missing is information");

        Write(dir, """{ "rules": [ { "exeName": "a.exe", "threads": [0] } ] }""");
        NewStore(dir, log).Load();
        Assert.True(log.Has(LogLevel.Warning, "rule at index 0 skipped: missing/invalid id"),
            "config.rule-skipped is warning");

        Write(dir, $$"""{ "rules": [ { "id": "{{IdA}}", "exeName": "a.exe", "threads": [0] } ] }""");
        NewStore(dir, log).Load();
        Assert.True(log.Has(LogLevel.Information, "config.json loaded, 1 rules, schemaVersion 1"),
            "config.loaded is information");
        Assert.True(log.Has(LogLevel.Debug, "config.skipped.json removed, no rules skipped on this load"),
            "config.skipped-file-removed is debug");
    }

    private static ConfigStore NewStore(TempDir dir, RecordingLog log) => new(dir.Path, log, new FakeClock());

    private static void Write(TempDir dir, string json)
        => File.WriteAllText(Path.Combine(dir.Path, ConfigFile), json);

    /// A path whose parent segment is a FILE — Directory.CreateDirectory throws IOException.
    private static string BlockedDirectory(TempDir dir)
    {
        string blocker = Path.Combine(dir.Path, "blocker");
        File.WriteAllText(blocker, "not a directory");
        return Path.Combine(blocker, "CorePin");
    }

    private static (ConfigLoadResult Loaded, RecordingLog Log) LoadJson(string json)
    {
        using var dir = new TempDir();
        var log = new RecordingLog();
        Write(dir, json);
        return (NewStore(dir, log).Load(), log);
    }

    private static int CorruptFiles(TempDir dir)
        => Directory.GetFiles(dir.Path, "config.corrupt-*.json").Length;

    private static int CorruptFiles(RecordingLog log) => log.Count("renamed to config.corrupt-");

    private static AppConfig SampleConfig(int logicalProcessors = 32) => new()
    {
        SchemaVersion = 1,
        Machine = new MachineInfo("Test CPU", logicalProcessors),
        Settings = new Settings(),
        Rules = RuleSet.Empty,
    };
}
