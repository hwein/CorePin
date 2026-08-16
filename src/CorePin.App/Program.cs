using System.Reflection;
using CorePin.Core.Diagnostics;
using CorePin.Core.Paths;
using CorePin.Core.Time;
using CorePin.Interop;

namespace CorePin.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var opts = StartupOptions.Parse(args);
        var paths = AppPaths.ForCurrentUser();
        IClock clock = new SystemClock();

        // Steps 0-2 (dumper, topology, group abort, single instance) and steps 4/4b
        // (configuration, logicalProcessors comparison) from S01 §3.7 arrive with
        // S04/S05/S07/S08.
        using var log = FileLog.Create(paths.LogDirectory,
                                       opts.LogLevelOverride ?? LogLevel.Info, clock);

        log.WriteAlways(LogLevel.Info, "app",
            $"CorePin {Version()} starting (flags: {FlagNames(opts)})");                // app.start

        foreach (var a in opts.Unknown)
            log.Warn("app", $"unknown argument '{a}' ignored");                         // app.unknown-argument
        if (opts.InvalidLogLevelValue is { } bad)
            log.Warn("app", $"unknown --log-level value '{bad}' ignored, config.json applies");  // app.log-level-invalid

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
            log.Warn("app", $"theme initialisation failed: {ex}");                      // app.theme-init-failed
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
        => opts.LogLevelOverride is null && opts.InvalidLogLevelValue is null
            ? "none"
            : "--log-level";
}
