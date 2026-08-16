using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace G0Theming;

/// Every number G0 reports comes from here: read off the running window or off a documented
/// API, never derived from a design constant.
internal static class Measure
{
    internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // -- header ---------------------------------------------------------------------------

    internal static void Header(StringBuilder sb, DemoWindow window, uint dpi, double scale)
    {
        RunOptions o = window.Options;
        sb.AppendLine("run.variant: " + o.Name);
        sb.AppendLine("run.themeMode: " + o.ThemeModeDescription);
        sb.AppendLine("run.themeFollowsSystem: " + Bool(o.FollowsSystemTheme));
        sb.AppendLine("run.backdropSwitch.name: " + RunOptions.BackdropSwitch);
        sb.AppendLine("run.backdropSwitch.requested: " + Bool(o.BackdropSwitchRequested));
        sb.AppendLine("run.backdropSwitch.readBackAfterSetSwitch: " + Bool(o.BackdropSwitchReadBack));
        sb.AppendLine("run.backdropSwitch.setVia: AppContext.SetSwitch in Main (no RuntimeHostConfigurationOption in the .csproj)");
        sb.AppendLine("run.timestampUtc: " + DateTime.UtcNow.ToString("O", Inv));
        sb.AppendLine("run.os: " + Environment.OSVersion.VersionString);
        sb.AppendLine("run.runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        sb.AppendLine("run.window.dpi: " + dpi.ToString(Inv));
        sb.AppendLine("run.window.scalePercent: " + (scale * 100).ToString("0.##", Inv));
        sb.AppendLine("run.system.appsUseLightTheme: " + Bool(window.Theme.SystemAppUsesLightTheme));
        sb.AppendLine("run.system.systemUsesLightTheme: " + Bool(window.Theme.SystemUsesLightTheme));
        sb.AppendLine("run.appliedTheme: " + (window.Theme.IsLight ? "light" : "dark"));
        sb.AppendLine("run.accentColor: " + Hex(SystemColors.AccentColor));
        sb.AppendLine("run.titleBar.lastDwmSetWindowAttributeHResult: 0x"
            + window.Theme.LastTitleBarHResult.ToString("X8", Inv));
        sb.AppendLine();
    }

    // -- measurement 1 --------------------------------------------------------------------

    internal static void Frame(StringBuilder sb, IntPtr hwnd, double scale)
    {
        sb.AppendLine("## Measurement 1 — window frame at the real window (A-S10-1, S10 §14.1)");

        NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT wr);
        NativeMethods.GetClientRect(hwnd, out NativeMethods.RECT cr);
        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(hwnd, ref origin);

        sb.AppendLine("m1.getWindowRect.px: " + Rect(wr));
        sb.AppendLine("m1.clientRect.size.px: " + cr.Width.ToString(Inv) + "x" + cr.Height.ToString(Inv));
        sb.AppendLine("m1.clientOriginOnScreen.px: " + origin.X.ToString(Inv) + "," + origin.Y.ToString(Inv));

        Sides(sb, "m1.getWindowRect",
            origin.X - wr.Left,
            wr.Right - (origin.X + cr.Width),
            wr.Bottom - (origin.Y + cr.Height),
            origin.Y - wr.Top,
            scale);

        int hr = NativeMethods.DwmGetWindowAttribute(
            hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT efb,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>());
        sb.AppendLine("m1.extendedFrameBounds.hresult: 0x" + hr.ToString("X8", Inv));
        if (hr == 0)
        {
            sb.AppendLine("m1.extendedFrameBounds.px: " + Rect(efb));
            Sides(sb, "m1.extendedFrameBounds",
                origin.X - efb.Left,
                efb.Right - (origin.X + cr.Width),
                efb.Bottom - (origin.Y + cr.Height),
                origin.Y - efb.Top,
                scale);
            sb.AppendLine("m1.invisibleResizeBorder.left.px: " + (efb.Left - wr.Left).ToString(Inv));
            sb.AppendLine("m1.invisibleResizeBorder.right.px: " + (wr.Right - efb.Right).ToString(Inv));
            sb.AppendLine("m1.invisibleResizeBorder.bottom.px: " + (wr.Bottom - efb.Bottom).ToString(Inv));
            sb.AppendLine("m1.invisibleResizeBorder.top.px: " + (efb.Top - wr.Top).ToString(Inv));
        }

        sb.AppendLine("m1.note: GetWindowRect includes the invisible drag border of a CanResize "
            + "window; DWMWA_EXTENDED_FRAME_BOUNDS is the frame the compositor actually paints.");
        sb.AppendLine();
    }

    private static void Sides(
        StringBuilder sb, string prefix, double left, double right, double bottom, double top, double scale)
    {
        sb.AppendLine(prefix + ".frame.left.px: " + left.ToString("0.##", Inv)
            + "  .dip: " + (left / scale).ToString("0.##", Inv));
        sb.AppendLine(prefix + ".frame.right.px: " + right.ToString("0.##", Inv)
            + "  .dip: " + (right / scale).ToString("0.##", Inv));
        sb.AppendLine(prefix + ".frame.bottom.px: " + bottom.ToString("0.##", Inv)
            + "  .dip: " + (bottom / scale).ToString("0.##", Inv));
        sb.AppendLine(prefix + ".captionPlusTopFrame.px: " + top.ToString("0.##", Inv)
            + "  .dip: " + (top / scale).ToString("0.##", Inv));

        double maxSide = Math.Max(left, Math.Max(right, bottom)) / scale;
        sb.AppendLine(prefix + ".maxSide.dip: " + maxSide.ToString("0.##", Inv));
        sb.AppendLine(prefix + ".verdict.perSideAtMost9Dip: " + Bool(maxSide <= 9.0));
    }

    internal static void MetricsByDpi(StringBuilder sb, uint actualDpi)
    {
        sb.AppendLine("## Measurement 1b — GetSystemMetricsForDpi for 100 / 125 / 150 %");
        sb.AppendLine("m1b.note: DPI-parameterised documented API — no display setting was changed.");

        foreach (uint dpi in new uint[] { 96, 120, 144 })
        {
            double scale = dpi / 96.0;
            int cxSize = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSIZEFRAME, dpi);
            int cySize = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CYSIZEFRAME, dpi);
            int padded = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXPADDEDBORDER, dpi);
            int caption = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CYCAPTION, dpi);

            string p = "m1b.dpi" + dpi.ToString(Inv);
            sb.AppendLine(p + ".scalePercent: " + (scale * 100).ToString("0.##", Inv));
            sb.AppendLine(p + ".SM_CXSIZEFRAME.px: " + cxSize.ToString(Inv)
                + "  .dip: " + (cxSize / scale).ToString("0.##", Inv));
            sb.AppendLine(p + ".SM_CXPADDEDBORDER.px: " + padded.ToString(Inv)
                + "  .dip: " + (padded / scale).ToString("0.##", Inv));
            sb.AppendLine(p + ".SM_CYSIZEFRAME.px: " + cySize.ToString(Inv)
                + "  .dip: " + (cySize / scale).ToString("0.##", Inv));
            sb.AppendLine(p + ".SM_CYCAPTION.px: " + caption.ToString(Inv)
                + "  .dip: " + (caption / scale).ToString("0.##", Inv));

            double horizontal = cxSize + padded;
            double vertical = cySize + padded;
            double captionTotal = caption + cySize + padded;
            sb.AppendLine(p + ".horizontalFrame.px: " + horizontal.ToString("0.##", Inv)
                + "  .dip: " + (horizontal / scale).ToString("0.##", Inv));
            sb.AppendLine(p + ".verticalFrame.px: " + vertical.ToString("0.##", Inv)
                + "  .dip: " + (vertical / scale).ToString("0.##", Inv));
            sb.AppendLine(p + ".captionPlusTopFrame.px: " + captionTotal.ToString("0.##", Inv)
                + "  .dip: " + (captionTotal / scale).ToString("0.##", Inv));
            sb.AppendLine(p + ".verdict.perSideAtMost9Dip: "
                + Bool(Math.Max(horizontal, vertical) / scale <= 9.0));
            sb.AppendLine(p + ".isTheDpiThatWasActuallyOnScreen: " + Bool(dpi == actualDpi));
        }

        sb.AppendLine();
    }

    // -- measurement 2 --------------------------------------------------------------------

    /// Async because the three surfaces have to be probed one at a time: the ToolTip overlaps
    /// the Popup, and a probe that samples the wrong overlay is worse than no probe.
    internal static async Task BackgroundAsync(
        StringBuilder sb, DemoWindow window, IntPtr hwnd, uint dpi, string filePrefix, string directory)
    {
        window.Panel.CloseToolTip();
        window.Panel.OpenFlyout();
        await Task.Delay(600);

        bool light = window.Theme.IsLight;
        string theme = light ? "light" : "dark";
        string p = "m2." + theme;

        NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT wr);
        NativeMethods.GetClientRect(hwnd, out NativeMethods.RECT cr);
        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(hwnd, ref origin);

        int backdropHr = NativeMethods.DwmGetWindowAttribute(
            hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, out int backdrop, sizeof(int));
        sb.AppendLine(p + ".backdropType.hresult: 0x" + backdropHr.ToString("X8", Inv));
        sb.AppendLine(p + ".backdropType.value: " + backdrop.ToString(Inv)
            + " (0=auto 1=none 2=mica 3=acrylic 4=tabbed)");

        CapturedImage screen = ScreenCapture.FromScreen(wr.Left, wr.Top, wr.Width, wr.Height);
        CapturedImage printed = ScreenCapture.FromWindow(hwnd, wr.Width, wr.Height);

        string screenFile = $"{filePrefix}-{theme}-{dpi.ToString(Inv)}.png";
        string printFile = $"{filePrefix}-{theme}-{dpi.ToString(Inv)}-printwindow.png";
        screen.Save(Path.Combine(directory, screenFile));
        printed.Save(Path.Combine(directory, printFile));
        sb.AppendLine(p + ".screenCapture.file: " + screenFile);
        sb.AppendLine(p + ".printWindow.file: " + printFile);

        var expected = (Color)Application.Current.Resources["CorePin.Color.WindowBackground"];
        sb.AppendLine(p + ".expected: " + Hex(expected) + " (CorePin.Color.WindowBackground)");

        int inset = Math.Max(2, (int)Math.Round(4 * dpi / 96.0));
        (string Name, int X, int Y)[] probes =
        {
            ("topLeft", inset, inset),
            ("topRight", cr.Width - inset, inset),
            ("bottomLeft", inset, cr.Height - inset),
            ("bottomCenter", cr.Width / 2, cr.Height - inset),
            ("rightMiddle", cr.Width - inset, cr.Height / 2),
        };

        int offsetX = origin.X - wr.Left;
        int offsetY = origin.Y - wr.Top;
        bool allMatch = true;

        foreach ((string name, int x, int y) in probes)
        {
            (byte r, byte g, byte b) = screen.At(offsetX + x, offsetY + y);
            (byte pr, byte pg, byte pb) = printed.At(offsetX + x, offsetY + y);
            bool match = r == expected.R && g == expected.G && b == expected.B;
            allMatch &= match;

            string q = p + ".probe." + name;
            sb.AppendLine(q + ".clientPx: " + x.ToString(Inv) + "," + y.ToString(Inv));
            sb.AppendLine(q + ".screen: " + Hex(r, g, b) + "  match: " + Bool(match)
                + "  deltaMax: " + MaxDelta(expected, r, g, b).ToString(Inv));
            sb.AppendLine(q + ".printWindow: " + Hex(pr, pg, pb));
        }

        sb.AppendLine(p + ".verdict.opaqueAtEveryProbe: " + Bool(allMatch));

        Flyout(sb, p, window, dpi, filePrefix, theme, directory);

        window.Panel.CloseFlyout();
        window.Panel.OpenToolTip();
        await Task.Delay(600);
        ToolTipSurface(sb, p, window, dpi, filePrefix, theme, directory);

        window.Panel.CloseToolTip();
        window.Panel.OpenFlyout();
        sb.AppendLine();
    }

    /// The Popup lives in its own visual tree and its own HWND — S03 §11 points 11/17 need it
    /// measured separately, not assumed to inherit the window's theme.
    private static void Flyout(
        StringBuilder sb, string p, DemoWindow window, uint dpi,
        string filePrefix, string theme, string directory)
    {
        string q = p + ".flyout";
        sb.AppendLine(q + ".isOpen: " + Bool(window.Panel.FlyoutIsOpen));

        FrameworkElement surface = window.Panel.FlyoutContent;
        if (!window.Panel.FlyoutIsOpen || !surface.IsVisible || surface.ActualWidth < 4)
        {
            sb.AppendLine(q + ".surface: <not rendered>");
            return;
        }

        var expected = (Color)Application.Current.Resources["CorePin.Color.CardBackground"];
        sb.AppendLine(q + ".expected: " + Hex(expected) + " (CorePin.Color.CardBackground)");
        ProbeSurface(sb, q, surface, expected,
            $"{filePrefix}-{theme}-{dpi.ToString(Inv)}-flyout.png", directory, compareToExpected: true);
    }

    /// The ToolTip background is not one of CorePin's tokens (it is the stock control's own
    /// chrome), so the value is reported plus an objective light/dark classification.
    private static void ToolTipSurface(
        StringBuilder sb, string p, DemoWindow window, uint dpi,
        string filePrefix, string theme, string directory)
    {
        string q = p + ".tooltip";
        sb.AppendLine(q + ".isOpen: " + Bool(window.Panel.ToolTipIsOpen));

        ToolTip? tip = window.Panel.SampleToolTip;
        if (tip is null || !window.Panel.ToolTipIsOpen || !tip.IsVisible || tip.ActualWidth < 4)
        {
            sb.AppendLine(q + ".surface: <not rendered>");
            return;
        }

        var windowBackground = (Color)Application.Current.Resources["CorePin.Color.WindowBackground"];
        sb.AppendLine(q + ".windowBackgroundForReference: " + Hex(windowBackground));
        ProbeSurface(sb, q, tip, windowBackground,
            $"{filePrefix}-{theme}-{dpi.ToString(Inv)}-tooltip.png", directory, compareToExpected: false);
    }

    private static void ProbeSurface(
        StringBuilder sb, string q, FrameworkElement surface, Color expected,
        string fileName, string directory, bool compareToExpected)
    {
        Point topLeft = surface.PointToScreen(new Point(0, 0));
        Point bottomRight = surface.PointToScreen(new Point(surface.ActualWidth, surface.ActualHeight));
        int x = (int)Math.Round(topLeft.X);
        int y = (int)Math.Round(topLeft.Y);
        int w = (int)Math.Round(bottomRight.X - topLeft.X);
        int h = (int)Math.Round(bottomRight.Y - topLeft.Y);

        sb.AppendLine(q + ".screenRect.px: L=" + x.ToString(Inv) + " T=" + y.ToString(Inv)
            + " (" + w.ToString(Inv) + "x" + h.ToString(Inv) + ")");
        sb.AppendLine(q + ".size.dip: " + surface.ActualWidth.ToString("0.##", Inv)
            + "x" + surface.ActualHeight.ToString("0.##", Inv));

        if (w < 4 || h < 4)
        {
            sb.AppendLine(q + ".surface: <degenerate rect>");
            return;
        }

        CapturedImage shot = ScreenCapture.FromScreen(x, y, w, h);
        shot.Save(Path.Combine(directory, fileName));
        sb.AppendLine(q + ".file: " + fileName);

        // Inside the 1 px outline and the 8 DIP padding, so the probes land on the surface
        // itself, not on glyphs.
        (string Name, int X, int Y)[] points =
        {
            ("topLeft", 3, 3),
            ("topRight", w - 4, 3),
            ("bottomLeft", 3, h - 4),
            ("bottomRight", w - 4, h - 4),
        };

        bool allMatch = true;
        int luminanceSum = 0;
        foreach ((string name, int px, int py) in points)
        {
            (byte r, byte g, byte b) = shot.At(px, py);
            bool match = r == expected.R && g == expected.G && b == expected.B;
            allMatch &= match;
            luminanceSum += (r * 299 + g * 587 + b * 114) / 1000;
            sb.AppendLine(q + ".probe." + name + ": " + Hex(r, g, b)
                + (compareToExpected
                    ? "  match: " + Bool(match) + "  deltaMax: " + MaxDelta(expected, r, g, b).ToString(Inv)
                    : string.Empty));
        }

        int meanLuminance = luminanceSum / points.Length;
        sb.AppendLine(q + ".meanLuminance: " + meanLuminance.ToString(Inv)
            + "  classified: " + (meanLuminance >= 128 ? "light" : "dark"));
        if (compareToExpected) sb.AppendLine(q + ".verdict.matchesCardBackground: " + Bool(allMatch));
    }

    private static int MaxDelta(Color expected, byte r, byte g, byte b) =>
        Math.Max(Math.Abs(expected.R - r), Math.Max(Math.Abs(expected.G - g), Math.Abs(expected.B - b)));

    // -- measurement 3 --------------------------------------------------------------------

    internal static void FontsReport(StringBuilder sb)
    {
        sb.AppendLine("## Measurement 3 — font resolution (S03 §11 point 18, §8.1)");

        string[] wanted = { "Segoe UI Variable Text", "Segoe UI Variable Display", "Segoe UI", "Cascadia Mono" };
        foreach (string name in wanted)
        {
            bool installed = Fonts.SystemFontFamilies.Any(f =>
                string.Equals(f.Source, name, StringComparison.OrdinalIgnoreCase)
                || f.FamilyNames.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)));
            sb.AppendLine("m3.systemFontFamilies.contains[" + name + "]: " + Bool(installed));
        }

        foreach (string name in wanted) ProbeFamily(sb, name);

        sb.AppendLine("m3.tokenChain.ui: " + ((FontFamily)Application.Current.Resources["CorePin.Font.UI"]).Source);
        sb.AppendLine("m3.tokenChain.heading: " + ((FontFamily)Application.Current.Resources["CorePin.Font.Heading"]).Source);
        sb.AppendLine("m3.tokenChain.mono: " + ((FontFamily)Application.Current.Resources["CorePin.Font.Mono"]).Source);
        sb.AppendLine("m3.note: only FontUri values that were actually read are reported; nothing "
            + "is claimed about variable-font axes, which this probe cannot see.");
        sb.AppendLine();
    }

    private static void ProbeFamily(StringBuilder sb, string familyName)
    {
        string p = "m3.family[" + familyName + "]";
        var family = new FontFamily(familyName);
        sb.AppendLine(p + ".source: " + family.Source);
        sb.AppendLine(p + ".typefaceCount: " + family.GetTypefaces().Count.ToString(Inv));
        sb.AppendLine(p + ".familyNames: " + string.Join(
            " | ", family.FamilyNames.Select(kv => kv.Key.IetfLanguageTag + "=" + kv.Value)));

        (string Label, FontWeight Weight)[] weights =
        {
            ("regular", FontWeights.Normal),
            ("semibold", FontWeights.SemiBold),
            ("bold", FontWeights.Bold),
        };

        foreach ((string label, FontWeight weight) in weights)
        {
            var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            string q = p + "." + label;
            if (typeface.TryGetGlyphTypeface(out GlyphTypeface? glyph) && glyph is not null)
            {
                sb.AppendLine(q + ".fontUri: " + glyph.FontUri);
                sb.AppendLine(q + ".resolvedWeight: " + glyph.Weight);
                sb.AppendLine(q + ".faceName: "
                    + (glyph.FaceNames.Count > 0 ? glyph.FaceNames.Values.First() : "<none>"));
            }
            else
            {
                sb.AppendLine(q + ".fontUri: <unresolved>");
            }
        }
    }

    // -- layout ---------------------------------------------------------------------------

    internal static void Layout(StringBuilder sb, DemoWindow window)
    {
        sb.AppendLine("## Layout at the running window");
        sb.AppendLine("layout.window.actualSize.dip: "
            + window.ActualWidth.ToString("0.##", Inv) + "x" + window.ActualHeight.ToString("0.##", Inv));
        sb.AppendLine("layout.cell.what: smtHalf and singleCell are the Border elements that "
            + "carry the MouseLeftButtonDown handler — the clickable face itself, not a shell "
            + "around it. The 1 px outline sits on the face, so its outer box IS the hit area. "
            + "smtCellPair is the 20+4+20 container and is not clickable.");
        ElementSize(sb, "layout.cell.smtCellPair", window.Panel.FirstSmtShell);
        ElementSize(sb, "layout.cell.smtHalf", window.Panel.FirstSmtHalf);
        HitTestFlags(sb, "layout.cell.smtHalf", window.Panel.FirstSmtHalf);
        ElementSize(sb, "layout.cell.singleCell", window.Panel.FirstSingleCell);
        HitTestFlags(sb, "layout.cell.singleCell", window.Panel.FirstSingleCell);
        ElementSize(sb, "layout.ruleRow", window.Panel.FirstRuleRow);
        sb.AppendLine("layout.ruleList.verticalScrollBarVisible: " + Bool(window.Panel.RuleListScrolls));
        sb.AppendLine("layout.cardArea.verticalScrollBarVisible: " + Bool(window.Panel.CardScrolls));
        sb.AppendLine();
    }

    /// Rendered size in DIP and in real device pixels (PointToScreen) — the pair S03 §11
    /// point 6 asks for: 20 x 28 DIP means 25 x 35 px at 125 % and 30 x 42 px at 150 %.
    internal static void ElementSize(StringBuilder sb, string prefix, FrameworkElement? element)
    {
        if (element is null || !element.IsVisible)
        {
            sb.AppendLine(prefix + ": <not rendered>");
            return;
        }

        Point topLeft = element.PointToScreen(new Point(0, 0));
        Point bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        sb.AppendLine(prefix + ".dip: "
            + element.ActualWidth.ToString("0.##", Inv) + "x" + element.ActualHeight.ToString("0.##", Inv)
            + "  .devicePx: "
            + (bottomRight.X - topLeft.X).ToString("0.##", Inv) + "x"
            + (bottomRight.Y - topLeft.Y).ToString("0.##", Inv));
    }

    private static void HitTestFlags(StringBuilder sb, string prefix, UIElement? element)
    {
        if (element is null) return;
        sb.AppendLine(prefix + ".isHitTestVisible: " + Bool(element.IsHitTestVisible));
    }

    // -- helpers --------------------------------------------------------------------------

    internal static string Bool(bool value) => value ? "true" : "false";

    private static string Rect(NativeMethods.RECT r) =>
        $"L={r.Left.ToString(Inv)} T={r.Top.ToString(Inv)} R={r.Right.ToString(Inv)} B={r.Bottom.ToString(Inv)} ({r.Width.ToString(Inv)}x{r.Height.ToString(Inv)})";

    internal static string Hex(Color c) => Hex(c.R, c.G, c.B);

    internal static string Hex(byte r, byte g, byte b) =>
        "#" + r.ToString("X2", Inv) + g.ToString("X2", Inv) + b.ToString("X2", Inv);

    internal static string ResolveDirectory()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "G0Theming.csproj")))
        {
            dir = dir.Parent;
        }

        string path = Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, "measurements");
        Directory.CreateDirectory(path);
        return path;
    }
}
