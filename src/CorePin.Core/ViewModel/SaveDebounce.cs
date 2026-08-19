using CorePin.Core.Configuration;

namespace CorePin.Core.ViewModel;

/// The 500 ms save debounce without its own timer: the caller wires the deadline. UI thread only.
public sealed class SaveDebounce
{
    public const int DelayMs = 500;

    private readonly Action<AppConfig> _save;
    private readonly Action _startDeadline;
    private readonly Action _stopDeadline;

    private AppConfig? _pending;

    public SaveDebounce(Action<AppConfig> save, Action startDeadline, Action stopDeadline)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(startDeadline);
        ArgumentNullException.ThrowIfNull(stopDeadline);

        _save = save;
        _startDeadline = startDeadline;
        _stopDeadline = stopDeadline;
    }

    public bool HasPendingSave => _pending is not null;

    /// Each call pushes the deadline back; only the newest config survives.
    public void RequestSave(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _pending = config;
        _stopDeadline();
        _startDeadline();
    }

    public void OnTimerFired() => Write();

    public void FlushNow() => Write();

    private void Write()
    {
        _stopDeadline();
        if (_pending is not { } config) return;

        _pending = null;
        _save(config);
    }
}
