using CorePin.Core.Autostart;

namespace CorePin.Interop;

/// What the autostart submenu shows: the caller reads Windows, TrayMenu only draws.
public sealed record TrayMenuState(
    AutostartMode Mode, bool AdminSelectable, bool AdminStale, bool NormalDisabledInTaskManager);
