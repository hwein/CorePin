using System;
using System.Runtime.CompilerServices;

namespace G0Theming;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        RunOptions options = RunOptions.Parse(args);

        // Must run before the first WPF type is touched — WPF reads the switch once and caches
        // it. Nothing above this line references a WPF type; Start is NoInlining so the JIT
        // does not pull PresentationFramework in while compiling Main.
        AppContext.SetSwitch(RunOptions.BackdropSwitch, options.BackdropSwitchRequested);
        AppContext.TryGetSwitch(RunOptions.BackdropSwitch, out bool readBack);
        options.BackdropSwitchReadBack = readBack;

        return Start(options);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Start(RunOptions options)
    {
        AppBase app;
        if (options.UsesApplicationThemeMode)
        {
            var fluent = new FluentApp(options);
            fluent.InitializeComponent();
            app = fluent;
        }
        else
        {
            var plain = new App(options);
            plain.InitializeComponent();
            app = plain;
        }

        // With Application.ThemeMode="System" the Fluent theme follows the OS, so the token
        // dictionaries have to start there too — forcing the other one would compare a window
        // whose stock controls and whose own surfaces disagree.
        bool startLight = options.FollowsSystemTheme
            ? ThemeSettings.ReadAppUsesLightTheme()
            : !options.StartDark;
        app.Theme.Initialize(app, startLight);

        DemoWindow window = options.UsesWindowThemeMode
            ? new FluentWindow(options, app.Theme)
            : new MainWindow(options, app.Theme);

        app.Theme.RegisterWindow(window);
        return app.Run(window);
    }
}
