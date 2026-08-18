using CorePin.Core.Diagnostics;
using CorePin.Core.Platform;
using CorePin.Core.Rules;

namespace CorePin.Core.Engine;

internal readonly record struct PinOutcome(int Matched, int Affected, int FirstPid, BlockReason Reason);

/// Open, reconfirm the name, read, set, close — once per hit of one rule.
internal sealed class ProcessPinner(IAffinityAccess access, ILog log, PinList pins)
{
    private enum PinResult { NoHit, Pinned, NeedsAdminRights, BlockedByWindows }

    internal PinOutcome Run(Rule rule, IReadOnlyList<ProcessEntry> matches)
    {
        int matched = 0;
        int affected = 0;
        int firstPid = 0;
        var reason = BlockReason.None;

        foreach (var process in matches)
        {
            var result = PinOne(rule, process);
            if (result == PinResult.NoHit) continue;

            matched++;
            if (firstPid == 0) firstPid = process.Pid;

            if (result == PinResult.Pinned) affected++;
            else if (result == PinResult.NeedsAdminRights) reason = BlockReason.NeedsAdminRights;
            else if (reason == BlockReason.None) reason = BlockReason.BlockedByWindows;
        }

        return new PinOutcome(matched, affected, firstPid, reason);
    }

    private PinResult PinOne(Rule rule, ProcessEntry process)
    {
        var opened = access.Open(process.Pid);
        if (opened.Failure == OpenFailure.Gone)
        {
            log.Debug("engine", $"rule '{rule.ExeName}': PID {process.Pid} gone before it could be opened");
            return PinResult.NoHit;
        }

        var handle = opened.Handle;
        if (opened.Failure != OpenFailure.None || handle is null)
        {
            if (log.IsEnabled(LogLevel.Debug))
                log.Debug("engine", $"rule '{rule.ExeName}': OpenProcess PID {process.Pid} failed, " +
                                    $"Win32 {opened.Win32Error} {Win32ErrorNames.Of(opened.Win32Error)}");
            return PinResult.NeedsAdminRights;
        }

        using (handle)
        {
            string? confirmed = handle.QueryExeName();
            if (confirmed is null || !string.Equals(confirmed, rule.ExeName, StringComparison.OrdinalIgnoreCase))
            {
                log.Debug("engine", $"PID {process.Pid}: name changed since enumeration " +
                                    $"('{process.ExeName}' -> '{confirmed ?? "?"}'), handle discarded");
                return PinResult.NoHit;
            }

            var startUtc = handle.QueryStartTimeUtc();
            if (startUtc is null) return PinResult.BlockedByWindows;

            var pair = handle.GetAffinity();
            if (pair is null) return PinResult.BlockedByWindows;

            if (log.IsEnabled(LogLevel.Debug))
                log.Debug("engine", $"rule '{rule.ExeName}': PID {process.Pid} " +
                                    $"want={rule.Threads.ToHex()} have={pair.Value.Process.ToHex()}");

            if (pair.Value.Process != rule.Threads && !handle.SetAffinity(rule.Threads))
            {
                if (log.IsEnabled(LogLevel.Debug))
                    log.Debug("engine", $"rule '{rule.ExeName}': SetAffinity PID {process.Pid} failed, " +
                                        $"Win32 {handle.LastError} {Win32ErrorNames.Of(handle.LastError)}");
                return PinResult.BlockedByWindows;   // an existing entry is neither added nor removed
            }

            pins.Set(process.Pid, new PinEntry(rule.Id, rule.ExeName, startUtc.Value));
            return PinResult.Pinned;
        }
    }
}
