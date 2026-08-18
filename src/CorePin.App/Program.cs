using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CorePin.Core.Configuration;
using CorePin.Core.Diagnostics;
using CorePin.Core.Engine;
using CorePin.Core.Paths;
using CorePin.Core.Platform;
using CorePin.Core.Time;
using CorePin.Core.Topology;
using CorePin.Interop;

namespace CorePin.App;

internal static class Program
{
    [STAThread]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1859:Use concrete types when possible for improved performance",
        Justification = "The #if DEBUG decorators reassign this port type in a debug build.")]
    private static int Main(string[] args)
    {
        var opts = StartupOptions.Parse(args);
        var paths = AppPaths.ForCurrentUser();
        IClock clock = new SystemClock();

        ITopologySource source = new Win32TopologySource();
#if DEBUG
        if (opts.DebugTopologyFile is { } file) source = new FileTopologySource(file);
        if (opts.DebugGroupCount is { } count) source = new GroupCountOverride(source, count);
#endif

        // 0. Dumper: before everything else — no WPF, no mutex, no logger, no directories.
        if (opts.DumpTopology)
            return DumpTopologyCommand.Run(source, opts, NullLog.Instance);

        // 1. Topology. The try reaches BEYOND Build: a format error gets the same box, code 4.
        CpuTopology topology;
        try
        {
            var snapshot = source.Read();

            //    Via MessageBoxW: WPF is never initialised and nothing is written.
            if (snapshot.ActiveGroupCount > 1)
            {
                MessageBoxes.ShowGroupLimit(snapshot);
                return 3;
            }
            topology = ClusterBuilder.Build(snapshot);
        }
        catch (Exception ex) { MessageBoxes.ShowTopologyReadFailed(ex); return 4; }

        // 2. Single instance is not implemented yet.
        using var log = FileLog.Create(paths.LogDirectory,
                                       opts.LogLevelOverride ?? LogLevel.Information, clock);

        log.WriteAlways(LogLevel.Information, "app",
            $"CorePin {Version()} starting (flags: {FlagNames(opts)})");
        log.WriteAlways(LogLevel.Information, "topology", Summarize(topology));
        if (log.IsEnabled(LogLevel.Debug))
            log.Debug("topology", FullDump(topology));

        //    The source ran before the logger and collected these instead of writing them.
        foreach (var w in source.Warnings) log.Warning("topology", w);

        foreach (var a in opts.Unknown)
            log.Warning("app", $"unknown argument '{a}' ignored");
        if (opts.InvalidLogLevelValue is { } bad)
            log.Warning("app", $"unknown --log-level value '{bad}' ignored, config.json applies");

        var guard = WriteGuard.Open;
#if DEBUG
        // 3b. Only here: this safeguard needs the logger and the REAL processor count.
        if (opts.DebugTopologyFile is not null)
        {
            log.Warning("app",
                $"debug switch active: --debug-topology {Path.GetFileName(opts.DebugTopologyFile)}");
            int real;
            try { real = ClusterBuilder.Build(new Win32TopologySource().Read()).LogicalProcessorCount; }
            catch (Exception ex)
            {
                log.Critical("app", $"real topology unreadable: {ex}");
                MessageBoxes.ShowTopologyReadFailed(ex);
                return 4;
            }
            if (topology.LogicalProcessorCount > real)
            {
                log.Warning("app", string.Create(CultureInfo.InvariantCulture,
                    $"fixture reports {topology.LogicalProcessorCount} LP, machine has {real} — switch rejected"));
                MessageBoxes.ShowFixtureTooLarge(topology.LogicalProcessorCount, real);
                return 5;
            }
            //     Must not persist rules built against foreign hardware.
            guard = WriteGuard.Strictest(guard, WriteGuard.NoPersist);
        }
        if (opts.DebugGroupCount is { } groups)
            log.Warning("app", string.Create(CultureInfo.InvariantCulture,
                $"debug switch active: --debug-groups {groups}"));
#endif

        // 4. Load the configuration.
        var config = new ConfigStore(paths.ConfigDirectory, log, clock);
        var loaded = config.Load();

        if (opts.LogLevelOverride is { } forced)
        {
            log.Minimum = forced;
            log.WriteAlways(LogLevel.Information, "app",
                $"--log-level {LogLevelNames.Format(forced)} overrides config.json settings.logLevel for this session");
        }
        else
        {
            log.Minimum = loaded.Config.Settings.LogLevel;
        }

        if (loaded.Outcome == ConfigLoadOutcome.TooNew)
            guard = WriteGuard.Strictest(guard, WriteGuard.ReadOnly);
        if (loaded.Outcome == ConfigLoadOutcome.Unreadable)
            guard = WriteGuard.Strictest(guard, WriteGuard.Unreadable);
        if (!guard.CanPersist)
            config.BlockWrites(guard.Reason);

        // 4b. The source is loaded.RawRules — loaded.Config.Rules is ALWAYS empty.
        var rules = loaded.RawRules;
        if (loaded.Config.Machine.LogicalProcessors != topology.LogicalProcessorCount)
        {
            rules = rules.MarkAllForReview();
            log.Warning("config", string.Create(CultureInfo.InvariantCulture,
                $"logicalProcessors changed: {loaded.Config.Machine.LogicalProcessors} -> {topology.LogicalProcessorCount}, all rules marked Needs review"));
        }

        // 5./6./7. Tray, watcher, window. Only the watcher exists yet; `rules` and `guard`
        //    are built here but not yet handed on to App.
        var engine = new AffinityEngine(new ProcessInventory(), new AffinityAccess(), clock, log,
                                        topology.MachineMask);
        using var watcher = new EngineHost(engine, clock, log, loaded.Config.Settings.PollIntervalMs);
        watcher.Submit(rules, new RuleChange(RuleChangeKind.None, Guid.Empty));
        watcher.Start();

        var app = new App();
        app.InitializeComponent();
        try
        {
            app.Theme.Initialize(app);
        }
        catch (Exception ex)
        {
            // Fatal either way — but the reason has to be in the log first.
            log.Critical("app", $"theme initialisation failed: {ex}");
            throw;
        }

        int exitCode = app.Run();
        watcher.Stop();

        log.WriteAlways(LogLevel.Information, "app", $"CorePin exiting (code {exitCode})");
        return exitCode;
    }

    private static string Version()
    {
        string? informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational)) return "0.0.0";

        // The SDK appends "+<commit>" when a source revision is known; not part of it.
        int plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }

    /// Flag names only, never values or paths; derived from PRESENCE, not a successful parse.
    private static string FlagNames(StartupOptions opts)
    {
        var flags = new List<string>();
        if (opts.Tray) flags.Add("--tray");
        if (opts.DumpTopology) flags.Add("--dump-topology");
        if (opts.LogLevelOverride is not null || opts.InvalidLogLevelValue is not null)
            flags.Add("--log-level");
#if DEBUG
        if (opts.DebugTopologyFile is not null) flags.Add("--debug-topology");
        if (opts.DebugGroupCount is not null) flags.Add("--debug-groups");
        if (opts.DebugDumpRaw) flags.Add("--debug-dump-raw");
#endif
        return flags.Count == 0 ? "none" : string.Join(", ", flags);
    }

    private static string Summarize(CpuTopology topology)
        => string.Create(CultureInfo.InvariantCulture,
            $"{topology.Vendor} {topology.CpuName} * {topology.LogicalProcessorCount} logical processors * "
            + $"{topology.Clusters.Count} clusters ({string.Join(", ", topology.Clusters.Select(c => c.Label))}) * "
            + $"{(topology.Profiling == ProfilingLevel.Profiled ? "profiled" : "not profiled")}");

    /// Deliberately NOT a serializer in CorePin.Core.Topology — that would be a second format.
    private static string FullDump(CpuTopology topology)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer,
                   new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("vendor", topology.Vendor);
            writer.WriteString("cpuName", topology.CpuName);
            writer.WriteNumber("logicalProcessorCount", topology.LogicalProcessorCount);
            writer.WriteString("machineMask", topology.MachineMask.ToHex());
            writer.WriteBoolean("hasSmt", topology.HasSmt);
            writer.WriteString("profiling", topology.Profiling.ToString());

            writer.WriteStartArray("clusters");
            foreach (var cluster in topology.Clusters)
            {
                writer.WriteStartObject();
                writer.WriteString("label", cluster.Label);
                if (cluster.Badge is { } badge) writer.WriteString("badge", badge);
                else writer.WriteNull("badge");
                writer.WriteNumber("l3Bytes", cluster.L3Bytes);
                writer.WriteBoolean("hasL3", cluster.HasL3);
                writer.WriteStartArray("cores");
                foreach (var core in cluster.Cores)
                {
                    writer.WriteStartArray();
                    foreach (int thread in core.Threads) writer.WriteNumberValue(thread);
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("notes");
            foreach (string note in topology.Notes) writer.WriteStringValue(note);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
