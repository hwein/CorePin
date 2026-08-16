using System;

namespace G0Theming;

public enum Variant
{
    /// Own dictionaries only, no ThemeMode anywhere (S03 §3.3).
    WegB,

    /// ThemeMode on the *window*, set declaratively in XAML, plus the backdrop switch.
    WegA,

    /// Same as WegA but without the backdrop switch — S03 §11 point 15.
    WegAOhneSwitch,

    /// ThemeMode="System" on the *Application*, declaratively in App XAML, never assigned in
    /// code. This is the shape CorePin would actually build (S03 D18).
    WegAApp,
}

public sealed class RunOptions
{
    public const string BackdropSwitch = "Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop";

    public Variant Variant { get; private init; }

    public bool Capture { get; private init; }

    public bool Watch { get; private init; }

    public bool StartDark { get; private init; }

    /// The backdrop switch belongs to every Weg-A shape except the deliberate counter-test.
    public bool BackdropSwitchRequested =>
        Variant is Variant.WegA or Variant.WegAApp;

    /// What AppContext.TryGetSwitch reported back right after SetSwitch in Main.
    public bool BackdropSwitchReadBack { get; set; }

    /// ThemeMode declared on the Window (FluentWindow.xaml).
    public bool UsesWindowThemeMode => Variant is Variant.WegA or Variant.WegAOhneSwitch;

    /// ThemeMode declared on the Application (FluentApp.xaml).
    public bool UsesApplicationThemeMode => Variant == Variant.WegAApp;

    /// With Application.ThemeMode="System" the theme is the OS theme; the prototype must not
    /// force the other one, because that would desync Fluent from the token dictionaries.
    public bool FollowsSystemTheme => UsesApplicationThemeMode;

    /// --dark gets its own artefact name: it is the counter-test "started in the target theme,
    /// no runtime theme switch" and must not overwrite the normal run's evidence.
    public string Name => BaseName + (StartDark && !FollowsSystemTheme ? "-direktstart-dunkel" : string.Empty);

    private string BaseName => Variant switch
    {
        Variant.WegA => "weg-a",
        Variant.WegAOhneSwitch => "weg-a-ohne-switch",
        Variant.WegAApp => "weg-a-app",
        _ => "weg-b",
    };

    public string ThemeModeDescription => Variant switch
    {
        Variant.WegA => "Window.ThemeMode=Light (XAML), reassigned in code on every toggle",
        Variant.WegAOhneSwitch => "Window.ThemeMode=Light (XAML), reassigned in code on every toggle",
        Variant.WegAApp => "Application.ThemeMode=System (XAML), never assigned in code",
        _ => "none",
    };

    public static RunOptions Parse(string[] args)
    {
        var variant = Variant.WegB;
        bool capture = false;
        bool watch = false;
        bool startDark = false;

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--weg-a": variant = Variant.WegA; break;
                case "--weg-a-ohne-switch": variant = Variant.WegAOhneSwitch; break;
                case "--weg-a-app": variant = Variant.WegAApp; break;
                case "--weg-b": variant = Variant.WegB; break;
                case "--capture": capture = true; break;
                case "--watch": watch = true; break;
                case "--dark": startDark = true; break;
                default: throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        if (capture && watch) throw new ArgumentException("--capture and --watch are exclusive.");

        return new RunOptions
        {
            Variant = variant,
            Capture = capture,
            Watch = watch,
            StartDark = startDark,
        };
    }
}
