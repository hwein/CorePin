using CorePin.Core.Platform;
using CorePin.Core.Primitives;

namespace CorePin.Tests.Fakes;

/// Handles over the processes of a FakeProcessInventory. A process starts on the machine
/// mask; a set outside its system mask fails, which is how a job object is staged.
public sealed class FakeAffinityAccess : IAffinityAccess
{
    private const int InvalidParameter = 87;

    private readonly Lock _gate = new();
    private readonly FakeProcessInventory _inventory;
    private readonly AffinityMask _machineMask;
    private readonly Dictionary<int, AffinityMask> _process = [];
    private readonly Dictionary<int, AffinityMask> _system = [];
    private readonly Dictionary<int, (OpenFailure Failure, int Win32Error)> _openFailures = [];
    private readonly Dictionary<int, int> _setFailures = [];
    private readonly Dictionary<int, int> _startTimeFailures = [];
    private readonly Dictionary<int, int> _affinityFailures = [];
    private readonly Dictionary<int, string?> _handleNames = [];
    private readonly List<(int Pid, AffinityMask Mask)> _setCalls = [];
    private int _openCallCount;

    public FakeAffinityAccess(FakeProcessInventory inventory, AffinityMask machineMask)
    {
        _inventory = inventory;
        _machineMask = machineMask;
    }

    public IReadOnlyList<(int Pid, AffinityMask Mask)> SetCalls
    {
        get { lock (_gate) return [.. _setCalls]; }
    }

    public int OpenCallCount { get { lock (_gate) return _openCallCount; } }

    public void FailOpen(int pid, OpenFailure failure, int win32Error)
    {
        lock (_gate) _openFailures[pid] = (failure, win32Error);
    }

    public void FailSet(int pid, int win32Error)
    {
        lock (_gate) _setFailures[pid] = win32Error;
    }

    public void SetSystemMask(int pid, AffinityMask mask)
    {
        lock (_gate) _system[pid] = mask;
    }

    /// The PID now belongs to a foreign process: new start time, and the masks of a fresh start.
    public void RecycleAsNewProcess(int pid, DateTime newStartUtc)
    {
        lock (_gate)
        {
            _process.Remove(pid);
            _system.Remove(pid);
        }

        _inventory.SetStart(pid, newStartUtc);
    }

    public AffinityMask CurrentMask(int pid)
    {
        lock (_gate) return ProcessMask(pid);
    }

    /// Stages a foreign change of the process mask (T4) and the state after a restart (T10).
    public void SetProcessMask(int pid, AffinityMask mask)
    {
        lock (_gate) _process[pid] = mask;
    }

    /// The name the HANDLE reports; the enumeration keeps the name of the inventory.
    public void SetHandleName(int pid, string? exeName)
    {
        lock (_gate) _handleNames[pid] = exeName;
    }

    public void FailStartTime(int pid, int win32Error)
    {
        lock (_gate) _startTimeFailures[pid] = win32Error;
    }

    public void FailGetAffinity(int pid, int win32Error)
    {
        lock (_gate) _affinityFailures[pid] = win32Error;
    }

    public ProcessOpenResult Open(int pid)
    {
        lock (_gate)
        {
            _openCallCount++;

            if (_openFailures.TryGetValue(pid, out var staged))
                return new ProcessOpenResult(null, staged.Failure, staged.Win32Error);

            if (!_inventory.Knows(pid)) return new ProcessOpenResult(null, OpenFailure.Gone, InvalidParameter);

            return new ProcessOpenResult(new Handle(this, pid), OpenFailure.None, 0);
        }
    }

    private AffinityMask ProcessMask(int pid)
        => _process.TryGetValue(pid, out var mask) ? mask : _machineMask;

    private AffinityMask SystemMask(int pid)
        => _system.TryGetValue(pid, out var mask) ? mask : _machineMask;

    private sealed class Handle(FakeAffinityAccess access, int pid) : IProcessHandle
    {
        public int LastError { get; private set; }

        public string? QueryExeName()
        {
            lock (access._gate)
            {
                if (access._handleNames.TryGetValue(pid, out string? staged)) return staged;
                return access._inventory.Knows(pid) ? access._inventory.NameOf(pid) : null;
            }
        }

        public DateTime? QueryStartTimeUtc()
        {
            lock (access._gate)
            {
                if (access._startTimeFailures.TryGetValue(pid, out int error))
                {
                    LastError = error;
                    return null;
                }
                LastError = 0;
                return access._inventory.StartOf(pid);
            }
        }

        public AffinityPair? GetAffinity()
        {
            lock (access._gate)
            {
                if (access._affinityFailures.TryGetValue(pid, out int error))
                {
                    LastError = error;
                    return null;
                }
                LastError = 0;
                return new AffinityPair(access.ProcessMask(pid), access.SystemMask(pid));
            }
        }

        public bool SetAffinity(AffinityMask mask)
        {
            lock (access._gate)
            {
                if (access._setFailures.TryGetValue(pid, out int error))
                {
                    LastError = error;
                    return false;
                }
                if (!mask.FitsInto(access.SystemMask(pid)))
                {
                    LastError = InvalidParameter;
                    return false;
                }

                LastError = 0;
                access._setCalls.Add((pid, mask));
                access._process[pid] = mask;
                return true;
            }
        }

        public void Dispose() { }
    }
}
