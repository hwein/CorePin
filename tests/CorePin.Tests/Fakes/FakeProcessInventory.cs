using CorePin.Core.Platform;

namespace CorePin.Tests.Fakes;

/// The process world of the engine tests: name and start time per PID, no handle.
public sealed class FakeProcessInventory : IProcessInventory
{
    private readonly Lock _gate = new();
    private readonly List<int> _order = [];
    private readonly Dictionary<int, (string ExeName, DateTime StartUtc)> _processes = [];
    private Exception? _throwOnNextList;
    private int _listCallCount;

    public int ListCallCount { get { lock (_gate) return _listCallCount; } }

    public void Add(int pid, string exeName, DateTime startUtc)
    {
        lock (_gate)
        {
            if (!_processes.ContainsKey(pid)) _order.Add(pid);
            _processes[pid] = (exeName, startUtc);
        }
    }

    public void Remove(int pid)
    {
        lock (_gate)
        {
            _processes.Remove(pid);
            _order.Remove(pid);
        }
    }

    /// Stays armed until it is cleared with null: the ten-failure policy needs a fault that does not heal by itself.
    public void ThrowOnNextList(Exception? ex)
    {
        lock (_gate) _throwOnNextList = ex;
    }

    public IReadOnlyList<ProcessEntry> ListOwnSession()
    {
        lock (_gate)
        {
            _listCallCount++;
            if (_throwOnNextList is { } ex) throw ex;

            var result = new List<ProcessEntry>(_processes.Count);
            foreach (int pid in _order) result.Add(new ProcessEntry(pid, _processes[pid].ExeName));
            return result;
        }
    }

    internal bool Knows(int pid)
    {
        lock (_gate) return _processes.ContainsKey(pid);
    }

    internal string NameOf(int pid)
    {
        lock (_gate) return _processes[pid].ExeName;
    }

    internal DateTime StartOf(int pid)
    {
        lock (_gate) return _processes[pid].StartUtc;
    }

    internal void SetStart(int pid, DateTime startUtc)
    {
        lock (_gate) _processes[pid] = (_processes[pid].ExeName, startUtc);
    }
}
