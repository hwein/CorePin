using System.Windows.Threading;
using CorePin.Core.Configuration;
using CorePin.Core.ViewModel;

namespace CorePin.App;

/// The UI-thread half of the save debounce: SaveDebounce decides what is written, the timer
/// only keeps the deadline.
internal sealed class ConfigPersister
{
    private readonly SaveDebounce _debounce;
    private readonly DispatcherTimer _timer;

    public ConfigPersister(Action<AppConfig> save, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(SaveDebounce.DelayMs),
        };
        _debounce = new SaveDebounce(config => { save(config); Saved?.Invoke(); }, _timer.Start, _timer.Stop);
        _timer.Tick += (_, _) => _debounce.OnTimerFired();
    }

    /// Raised once a config actually reached the store, never for a coalesced request.
    public event Action? Saved;

    public void RequestSave(AppConfig config) => _debounce.RequestSave(config);

    public void FlushNow() => _debounce.FlushNow();
}
