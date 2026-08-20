using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CorePin.Core.Diagnostics;
using CorePin.Interop;

namespace CorePin.App;

/// The one loading path for every rule and picker icon. Jobs run throttled on short-lived
/// pool threads; results come back on the UI thread, so callers need no dispatcher code.
public static class RuleIconLoader
{
    /// Eight concurrent open/query/extract chains; the rest waits in arrival order.
    private static readonly SemaphoreSlim Gate = new(8, 8);

    private static ILog _log = NullLog.Instance;

    /// Wires the real log in; App.OnStartup calls this before the first icon job.
    public static void Initialize(ILog log) => _log = log;

    /// Path already known (rule list at startup, file dialog, resolved flyout row).
    public static void LoadFromPath(string lastKnownPath, Action<BitmapSource?> onLoaded)
    {
        ArgumentNullException.ThrowIfNull(lastKnownPath);
        ArgumentNullException.ThrowIfNull(onLoaded);

        Enqueue(() =>
        {
            var icon = LoadIcon(lastKnownPath);
            Post(() => onLoaded(icon));
        });
    }

    /// Path still unknown (flyout row): resolve it first, then the icon — one queue slot, not two.
    public static void LoadFromProcess(int pid, Action<string?, BitmapSource?> onLoaded)
    {
        ArgumentNullException.ThrowIfNull(onLoaded);

        Enqueue(() =>
        {
            string? path = ProcessPathResolver.TryResolveExePath(pid);
            var icon = path is null ? null : LoadIcon(path);
            Post(() => onLoaded(path, icon));
        });
    }

    private static void Enqueue(Action job) => Task.Run(async () =>
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try { job(); }
        catch (Exception ex)
        {
            // Expected failures already return null inside job() without throwing.
            _log.Warning("app", $"icon load failed unexpectedly ({ex.GetType().Name})");
        }
        finally { Gate.Release(); }
    });

    /// Background priority, same as the engine bridge: icons yield to actual interaction.
    private static void Post(Action callback)
        => Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, callback);

    private static WriteableBitmap? LoadIcon(string path)
    {
        using var handle = ExeIconExtractor.Extract(path);
        if (handle is null) return null;

        try
        {
            var raw = Imaging.CreateBitmapSourceFromHIcon(
                handle.DangerousGetHandle(), Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // The WriteableBitmap copy owns its pixels before the using block frees the icon handle.
            var owned = new WriteableBitmap(raw);
            owned.Freeze();
            return owned;
        }
        catch (COMException)
        {
            return null;   // an icon WIC cannot decode gets the placeholder, like no icon at all
        }
    }
}
