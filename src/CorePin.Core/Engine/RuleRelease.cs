using CorePin.Core.Diagnostics;
using CorePin.Core.Platform;

namespace CorePin.Core.Engine;

internal readonly record struct ReleaseOutcome(bool VisitedAny, List<int> Failed);

/// Resets every pinned process of one rule to its own process system mask.
internal sealed class RuleRelease(IAffinityAccess access, ILog log, PinList pins)
{
    /// The PIDs that stayed pinned. Open, start time, mask read and set share this path.
    internal ReleaseOutcome Run(Guid ruleId, ReleaseReason reason)
    {
        var failed = new List<int>();
        var entries = pins.Of(ruleId);

        foreach (var (pid, entry) in entries)
        {
            var opened = access.Open(pid);
            if (opened.Failure == OpenFailure.Gone)
            {
                pins.Remove(pid);
                log.Debug("engine", $"rule '{entry.ExeName}': PID {pid} gone before release, nothing to reset");
                continue;
            }

            var handle = opened.Handle;
            if (opened.Failure != OpenFailure.None || handle is null)
            {
                Fail(entry, pid, opened.Win32Error, failed);
                continue;
            }

            using (handle)
            {
                var startUtc = handle.QueryStartTimeUtc();
                if (startUtc is null)
                {
                    Fail(entry, pid, handle.LastError, failed);
                    continue;
                }

                if (startUtc.Value != entry.StartUtc)
                {
                    pins.Remove(pid);
                    log.Debug("engine", $"rule '{entry.ExeName}': PID {pid} start time mismatch, " +
                                        "treated as different process");
                    continue;
                }

                var pair = handle.GetAffinity();
                if (pair is null || !handle.SetAffinity(pair.Value.System))
                {
                    Fail(entry, pid, handle.LastError, failed);
                    continue;
                }

                pins.Remove(pid);
                log.Information("engine", $"rule '{entry.ExeName}': released PID {pid} ({TextOf(reason)})");
            }
        }

        return new ReleaseOutcome(entries.Count > 0, failed);
    }

    private void Fail(PinEntry entry, int pid, int win32Error, List<int> failed)
    {
        pins.Remove(pid);
        failed.Add(pid);
        log.Error("engine", $"rule '{entry.ExeName}': failed to release PID {pid}, " +
                            $"Win32 {win32Error} {Win32ErrorNames.Of(win32Error)} — process remains pinned");
    }

    private static string TextOf(ReleaseReason reason) => reason switch
    {
        ReleaseReason.Disabled => "disabled",
        ReleaseReason.Removed => "deleted",
        _ => "all threads selected",
    };
}
