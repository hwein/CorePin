using System.Windows;
using System.Windows.Media;

namespace CorePin.App.Themes;

/// Typed access to the XAML tokens for self-drawing code (S03 §10.2). Every call reads
/// live from Application.Current.Resources — a cached value would be a second truth
/// beside the ResourceDictionary.
public static class Tokens
{
    // Colours
    public static Color StatusApplied => Read<Color>("CorePin.Color.StatusApplied");
    public static Color StatusBlocked => Read<Color>("CorePin.Color.StatusBlocked");
    public static Color StatusIdle => Read<Color>("CorePin.Color.StatusIdle");
    public static Color CardBackground => Read<Color>("CorePin.Color.CardBackground");
    public static Color WindowBackground => Read<Color>("CorePin.Color.WindowBackground");
    public static Color TextPrimary => Read<Color>("CorePin.Color.TextPrimary");
    public static Color TextSecondary => Read<Color>("CorePin.Color.TextSecondary");
    public static Color Border => Read<Color>("CorePin.Color.Border");

    // Accent: WPF's own source (S03 §5.3), not a Tokens.xaml key. WPF supplies a fallback
    // (#0078D4) on every internal failure — no fallback of our own here, it would never
    // be reached.
    public static Color Accent => SystemColors.AccentColor;

    // Type
    public static FontFamily FontUI => Read<FontFamily>("CorePin.Font.UI");
    public static FontFamily FontHeading => Read<FontFamily>("CorePin.Font.Heading");
    public static FontFamily FontMono => Read<FontFamily>("CorePin.Font.Mono");
    public static double FontSizeUI => Read<double>("CorePin.FontSize.UI");
    public static double FontSizeHeading => Read<double>("CorePin.FontSize.Heading");
    public static double FontSizeMono => Read<double>("CorePin.FontSize.Mono");
    public static FontWeight FontWeightHeading => Read<FontWeight>("CorePin.FontWeight.Heading");

    // Grid, radii, hit targets — RadiusValue instead of Radius: S10 needs double, not
    // CornerRadius (S03 §4.3).
    public static double RadiusCoreCell => Read<double>("CorePin.RadiusValue.CoreCell");
    public static double Spacing4 => Read<double>("CorePin.Spacing.4");
    public static double Spacing8 => Read<double>("CorePin.Spacing.8");
    public static double Spacing12 => Read<double>("CorePin.Spacing.12");
    public static double HitTargetMinWidth => Read<double>("CorePin.HitTarget.MinWidth");
    public static double HitTargetMinHeight => Read<double>("CorePin.HitTarget.MinHeight");

    private static T Read<T>(string key) =>
        Application.Current.Resources[key] is T value
            ? value
            : throw new InvalidOperationException(
                $"Missing or mistyped token '{key}' — check Tokens.xaml/Light.xaml/Dark.xaml. " +
                "This error may only surface at startup (ValidateAll), never when a core " +
                "cell is drawn for the first time.");

    /// Called by ThemeController.Initialize AFTER ApplyTheme: reads every key once so a
    /// missing or mistyped token surfaces as a hard startup error instead of an
    /// InvalidCastException while redrawing the CPU map. Also checks that the
    /// deliberately duplicated radius pairs carry the same value.
    public static void ValidateAll()
    {
        _ = (StatusApplied, StatusBlocked, StatusIdle, CardBackground, WindowBackground,
             TextPrimary, TextSecondary, Border, FontUI, FontHeading, FontMono,
             FontSizeUI, FontSizeHeading, FontSizeMono, FontWeightHeading,
             RadiusCoreCell, Spacing4, Spacing8, Spacing12,
             HitTargetMinWidth, HitTargetMinHeight);

        ValidateRadiusPairConsistency("CoreCell");
        ValidateRadiusPairConsistency("Card");
        ValidateRadiusPairConsistency("Button");
        ValidateRadiusPairConsistency("Window");
    }

    private static void ValidateRadiusPairConsistency(string name)
    {
        var typed = (CornerRadius)Application.Current.Resources[$"CorePin.Radius.{name}"];
        var value = (double)Application.Current.Resources[$"CorePin.RadiusValue.{name}"];
        if (typed.TopLeft != value)
            throw new InvalidOperationException(
                $"CorePin.Radius.{name} ({typed.TopLeft}) and CorePin.RadiusValue.{name} " +
                $"({value}) disagree — align both entries in Tokens.xaml.");
    }
}
