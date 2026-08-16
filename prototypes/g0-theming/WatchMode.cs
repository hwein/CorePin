using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace G0Theming;

/// --watch: the window stays open and records a full pass whenever Windows reports a DPI or a
/// theme change. The prototype only reacts — it never changes a system setting. The user
/// switches the display scaling by hand; every step it lands on is documented.
internal sealed class WatchMode
{
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DPICHANGED = 0x02E0;

    private readonly DemoWindow _window;
    private readonly string _directory;
    private readonly string _filePath;
    private int _passCount;
    private bool _busy;
    private bool _pending;
    private string _lastTrigger = "startup";

    private WatchMode(DemoWindow window)
    {
        _window = window;
        _directory = Measure.ResolveDirectory();
        _filePath = Path.Combine(_directory, "watch-" + window.Options.Name + ".txt");
    }

    public static void Attach(DemoWindow window) => new WatchMode(window).Start();

    private void Start()
    {
        IntPtr hwnd = new WindowInteropHelper(_window).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);

        var header = new StringBuilder();
        header.AppendLine("# CorePin G0 theming prototype — watch log (S03 §11, all DPI steps)");
        header.AppendLine("watch.variant: " + _window.Options.Name);
        header.AppendLine("watch.startedUtc: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        header.AppendLine("watch.note: one pass at startup, then one on every WM_DPICHANGED and "
            + "every WM_SETTINGCHANGE/ImmersiveColorSet. Screenshots are prefixed watch- so they "
            + "cannot overwrite the --capture evidence.");
        header.AppendLine();
        File.AppendAllText(_filePath, header.ToString(), Utf8);

        UpdateStatus();
        _ = RunPassAsync("startup");
    }

    private static UTF8Encoding Utf8 => new(encoderShouldEmitUTF8Identifier: false);

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            if (msg == WM_DPICHANGED)
            {
                _ = RunPassAsync("WM_DPICHANGED");
            }
            else if (msg == WM_SETTINGCHANGE)
            {
                // 02 §12 class of pitfall: lParam is a string pointer, not a flag.
                string? area = lParam == IntPtr.Zero ? null : Marshal.PtrToStringUni(lParam);
                if (string.Equals(area, "ImmersiveColorSet", StringComparison.Ordinal))
                {
                    bool changed = _window.Theme.OnSystemThemeChanged();
                    _ = RunPassAsync("WM_SETTINGCHANGE/ImmersiveColorSet"
                        + (changed ? " (theme switched)" : " (theme unchanged)"));
                }
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText(_filePath, "hook.error: " + ex + Environment.NewLine, Utf8);
        }

        return IntPtr.Zero;
    }

    private async Task RunPassAsync(string trigger)
    {
        if (_busy)
        {
            _pending = true;
            return;
        }

        _busy = true;
        _lastTrigger = trigger;
        try
        {
            // WM_DPICHANGED arrives before WPF has resized and relaid out the tree.
            await Task.Delay(1200);
            await WritePassAsync(trigger);
        }
        catch (Exception ex)
        {
            File.AppendAllText(_filePath, "pass.error: " + ex + Environment.NewLine, Utf8);
        }
        finally
        {
            _busy = false;
            UpdateStatus();
        }

        if (_pending)
        {
            _pending = false;
            await RunPassAsync(trigger + " (coalesced)");
        }
    }

    private async Task WritePassAsync(string trigger)
    {
        IntPtr hwnd = new WindowInteropHelper(_window).Handle;
        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;

        _window.Panel.FocusRuleItem();
        _window.UpdateLayout();

        _passCount++;
        UpdateStatus();   // before the screenshot, so the shot shows the pass it belongs to
        var sb = new StringBuilder();
        sb.AppendLine("=== pass " + _passCount.ToString(CultureInfo.InvariantCulture)
            + " — trigger: " + trigger + " ===");
        Measure.Header(sb, _window, dpi, scale);
        Measure.Frame(sb, hwnd, scale);
        Measure.MetricsByDpi(sb, dpi);
        await Measure.BackgroundAsync(sb, _window, hwnd, dpi, "watch-" + _window.Options.Name, _directory);
        Measure.Layout(sb, _window);
        sb.AppendLine();

        File.AppendAllText(_filePath, sb.ToString(), Utf8);
        _window.Panel.CloseToolTip();
    }

    private void UpdateStatus()
    {
        IntPtr hwnd = new WindowInteropHelper(_window).Handle;
        uint dpi = hwnd == IntPtr.Zero ? 96 : NativeMethods.GetDpiForWindow(hwnd);
        string scale = (dpi / 96.0 * 100).ToString("0.##", CultureInfo.InvariantCulture);

        _window.Panel.ShowWatchStatus(
            "WATCH  ·  DPI " + dpi.ToString(CultureInfo.InvariantCulture) + " (" + scale + " %)"
            + "  ·  theme " + (_window.Theme.IsLight ? "light" : "dark")
            + "  ·  passes recorded: " + _passCount.ToString(CultureInfo.InvariantCulture)
            + "  ·  last trigger: " + _lastTrigger
            + "  ·  log: " + Path.GetFileName(_filePath)
            + "   —   change display scaling or the Windows theme; close the window when done.");
    }
}
