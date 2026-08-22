using System.Diagnostics;
using CorePin.Core.Platform;

namespace CorePin.Core.Autostart;

public static class AutostartTruth
{
    public static bool IsOwnTask(TaskState task, string ownSid)
        => task.Present && string.Equals(task.PrincipalSid, ownSid, StringComparison.OrdinalIgnoreCase);

    /// A disabled own task starts nothing, so the run key next to it still decides.
    public static AutostartMode Resolve(RunKeyState runKey, TaskState task, string ownSid)
    {
        if (IsOwnTask(task, ownSid) && task.Enabled) return AutostartMode.Admin;

        if (runKey.Value is not null && !runKey.Disabled) return AutostartMode.Normal;
        return AutostartMode.Off;
    }

    /// The mechanism follows the rights of the instance that was clicked in; CorePin never elevates.
    public static AutostartSwitch Switch(AutostartMode current, bool isElevated) => current switch
    {
        AutostartMode.Off => isElevated ? AutostartSwitch.TurnOnTask : AutostartSwitch.TurnOnRunKey,
        AutostartMode.Normal => AutostartSwitch.TurnOff,
        AutostartMode.Admin => isElevated ? AutostartSwitch.TurnOff : AutostartSwitch.RefuseNeedsAdmin,
        _ => throw new UnreachableException(),
    };

    public static string ModeName(AutostartMode mode) => mode switch
    {
        AutostartMode.Off => "off",
        AutostartMode.Normal => "normal",
        AutostartMode.Admin => "admin",
        _ => throw new UnreachableException(),
    };
}
