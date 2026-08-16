using System.Diagnostics;
using CorePin.Core.Platform;

namespace CorePin.Interop;

public sealed class ProcessInventory : IProcessInventory
{
    private readonly uint _ownSessionId;

    public ProcessInventory()
    {
        try { _ownSessionId = (uint)Process.GetCurrentProcess().SessionId; }
        catch (Exception) { _ownSessionId = uint.MaxValue; }   // never fall back to session 0
    }

    public IReadOnlyList<ProcessEntry> ListOwnSession()
    {
        var result = new List<ProcessEntry>(capacity: 384);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if ((uint)process.SessionId != _ownSessionId) continue;
                result.Add(new ProcessEntry(process.Id, process.ProcessName + ".exe"));
            }
        }
        return result;
    }
}
