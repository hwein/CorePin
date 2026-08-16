using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CorePin.Core.Diagnostics;
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
        Justification = "S01 §3.7 declares the topology source as the port type ITopologySource; "
                      + "in a debug build the two #if DEBUG decorators reassign it. In a release "
                      + "build the reassignments are gone, which is the only reason the analyzer "
                      + "sees a concrete type here.")]
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

        // 0. Dumper: before everything else. No WPF, no mutex, NO logger (NullLog), no
        //    directory creation, no abort on more than one group.
        if (opts.DumpTopology)
            return DumpTopologyCommand.Run(source, opts, NullLog.Instance);

        // 1. Topology. A read failure is the only startup failure without a log and without
        //    a window. The try block reaches BEYOND ClusterBuilder.Build (S04 T-O14): a
        //    TopologyFormatException from the cluster building gets the same message box and
        //    exit code 4 instead of the Windows crash dialog.
        CpuTopology topology;
        try
        {
            var snapshot = source.Read();

            //    Abort on more than one processor group (02 §4.5) — via MessageBoxW, so WPF
            //    is never initialised and nothing is written, not even a log file.
            if (snapshot.ActiveGroupCount > 1)
            {
                MessageBoxes.ShowGroupLimit(snapshot);
                return 3;
            }
            topology = ClusterBuilder.Build(snapshot);
        }
        catch (Exception ex) { MessageBoxes.ShowTopologyReadFailed(ex); return 4; }

        // 2. Single instance arrives with S08; steps 4/4b (configuration) with S05.
        // 3. Log file.
        using var log = FileLog.Create(paths.LogDirectory,
                                       opts.LogLevelOverride ?? LogLevel.Info, clock);

        log.WriteAlways(LogLevel.Info, "app",
            $"CorePin {Version()} starting (flags: {FlagNames(opts)})");                 // app.start
        log.WriteAlways(LogLevel.Info, "topology", Summarize(topology));                 // topology.summary
        if (log.IsEnabled(LogLevel.Debug))
            log.Debug("topology", FullDump(topology));                                   // topology.dump

        //    Warnings of the topology source (S04 §2.7, T-O4). The source ran in step 0,
        //    before the logger — it collected them instead of writing them. Passed through
        //    unchanged and in the order of occurrence.
        foreach (var w in source.Warnings) log.Warn("topology", w);                      // topology.source-warning

        foreach (var a in opts.Unknown)
            log.Warn("app", $"unknown argument '{a}' ignored");                          // app.unknown-argument
        if (opts.InvalidLogLevelValue is { } bad)
            log.Warn("app", $"unknown --log-level value '{bad}' ignored, config.json applies");  // app.log-level-invalid

#if DEBUG
        // 3b. Second safeguard of the debug switch, only here: it needs the logger and the
        //     REAL processor count that the fixture path replaced (S01 §5.6, N3).
        //     The WriteGuard.NoPersist part arrives with S05.
        if (opts.DebugTopologyFile is not null)
        {
            log.Warn("app",
                $"debug switch active: --debug-topology {Path.GetFileName(opts.DebugTopologyFile)}");  // app.debug-switch-active
            int real;
            try { real = ClusterBuilder.Build(new Win32TopologySource().Read()).LogicalProcessorCount; }
            catch (Exception ex)
            {
                log.Warn("app", $"real topology unreadable: {ex}");                      // app.debug-topology-unreadable
                MessageBoxes.ShowTopologyReadFailed(ex);
                return 4;
            }
            if (topology.LogicalProcessorCount > real)
            {
                log.Warn("app", string.Create(CultureInfo.InvariantCulture,
                    $"fixture reports {topology.LogicalProcessorCount} LP, machine has {real} — switch rejected"));  // app.debug-fixture-too-large
                MessageBoxes.ShowFixtureTooLarge(topology.LogicalProcessorCount, real);
                return 5;
            }
        }
        if (opts.DebugGroupCount is { } groups)
            log.Warn("app", string.Create(CultureInfo.InvariantCulture,
                $"debug switch active: --debug-groups {groups}"));                       // app.debug-switch-active
#endif

        if (opts.LogLevelOverride is { } forced)
        {
            log.Minimum = forced;
            // Level names are written lower case (S02 §8.1), unlike the enum member.
            log.WriteAlways(LogLevel.Info, "app",
                $"--log-level {forced.ToString().ToLowerInvariant()} overrides config.json settings.logLevel for this session");  // app.log-level-override
        }
        else
        {
            log.Minimum = LogLevel.Info;
        }

        // The App constructor stays parameterless until something below it reads the
        // topology (S09/S10); a parameter nobody reads would be invented surface.
        var app = new App();
        app.InitializeComponent();                   // loads App.xaml (resources, S03)
        try
        {
            app.Theme.Initialize(app);               // merge theme slot, validate tokens (S03)
        }
        catch (Exception ex)
        {
            // A missing or mistyped token stays a hard startup error (S03 §10.2) — but the
            // reason has to be in the log before the process goes down.
            log.Warn("app", $"theme initialisation failed: {ex}");                       // app.theme-init-failed
            throw;
        }

        int exitCode = app.Run();

        log.WriteAlways(LogLevel.Info, "app", $"CorePin exiting (code {exitCode})");     // app.exit
        return exitCode;
    }

    private static string Version()
    {
        string? informational = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational)) return "0.0.0";

        // The SDK appends "+<commit>" when a source revision is known — not part of the version.
        int plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }

    /// Flag names only, never values or paths (S02 §7.1). Derived from the PRESENCE of the
    /// argument, not from a successful parse — an invalid value was still a passed flag.
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

    /// Compact JSON of the CpuTopology for topology.dump (S02 §6). Deliberately built here
    /// in the app layer and not as a CpuTopology serializer in CorePin.Core.Topology — a
    /// reusable one would be a second format next to S04 §3.4 (S04 §10.5).
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
