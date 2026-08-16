using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace G0Theming;

/// --capture: one self-terminating run per variant, so the whole G0 sweep can be scripted.
internal static class CaptureRun
{
    public static async Task RunAsync(DemoWindow window)
    {
        var report = new StringBuilder();
        string directory = Measure.ResolveDirectory();
        try
        {
            await ExecuteAsync(window, report, directory);
        }
        catch (Exception ex)
        {
            report.AppendLine("error: " + ex);
        }
        finally
        {
            File.WriteAllText(
                Path.Combine(directory, window.Options.Name + ".txt"),
                report.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Application.Current.Shutdown();
        }
    }

    private static async Task ExecuteAsync(DemoWindow window, StringBuilder sb, string directory)
    {
        window.Topmost = true;      // capture only: keeps the window unoccluded for the BitBlt
        window.Activate();
        await Task.Delay(1500);

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;

        sb.AppendLine("# CorePin G0 theming prototype — measurement report (S03 §2.2, §11)");
        Measure.Header(sb, window, dpi, scale);
        Measure.Frame(sb, hwnd, scale);
        Measure.MetricsByDpi(sb, dpi);

        sb.AppendLine("## Measurement 2 — background / flyout / tooltip (S03 §11 points 11, 15, 17)");
        if (window.Options.FollowsSystemTheme)
        {
            sb.AppendLine("m2.note: Application.ThemeMode=System — the theme follows the OS and is "
                + "never assigned in code, so only the current system theme can be captured here. "
                + "The other theme is covered by --watch, driven by a real OS theme change.");
        }

        foreach (bool light in ThemeOrder(window.Options, window.Theme.IsLight))
        {
            bool flyoutSurvived = window.Panel.FlyoutIsOpen;
            window.Theme.SetLight(light);
            window.Panel.FocusRuleItem();
            await Task.Delay(1200);

            sb.AppendLine("m2." + (light ? "light" : "dark")
                + ".flyout.stillOpenAfterThemeSwitch: " + Measure.Bool(flyoutSurvived)
                + " (meaningful for the second pass only — the first pass opens it)");
            await Measure.BackgroundAsync(sb, window, hwnd, dpi, window.Options.Name, directory);
        }

        Measure.FontsReport(sb);
        Measure.Layout(sb, window);
    }

    private static bool[] ThemeOrder(RunOptions options, bool currentIsLight)
    {
        if (options.FollowsSystemTheme) return new[] { currentIsLight };
        return options.StartDark ? new[] { false, true } : new[] { true, false };
    }
}
