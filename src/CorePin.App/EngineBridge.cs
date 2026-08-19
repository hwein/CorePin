using System.Windows.Threading;
using CorePin.Core.Engine;
using CorePin.Core.ViewModel;

namespace CorePin.App;

/// The engine raises its events on the worker thread; this is the only class that subscribes.
internal sealed class EngineBridge
{
    public EngineBridge(EngineHost host, Dispatcher dispatcher, RuleListViewModel rules)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(rules);

        host.StatusChanged += s => Post(() => rules.ApplyStatus(s));
        host.Heartbeat += h => Post(() => rules.ApplyHeartbeat(h));
        host.Faulted += f => Post(() => rules.ApplyFault(f));

        void Post(Action action)
        {
            // BeginInvoke throws once the dispatcher is down; while exiting that is the normal case.
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
            try { dispatcher.BeginInvoke(DispatcherPriority.Background, action); }
            catch (TaskCanceledException) { /* the dispatcher went away in between */ }
        }
    }
}
