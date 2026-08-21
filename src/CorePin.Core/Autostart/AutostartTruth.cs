using System.Diagnostics;
using CorePin.Core.Platform;

namespace CorePin.Core.Autostart;

public static class AutostartTruth
{
    public static AutostartMode Resolve(RunKeyState runKey, TaskState task, string ownSid)
    {
        bool ownTask = task.Present && string.Equals(task.PrincipalSid, ownSid, StringComparison.OrdinalIgnoreCase);
        if (ownTask) return task.Enabled ? AutostartMode.Admin : AutostartMode.Off;

        if (runKey.Value is not null) return runKey.Disabled ? AutostartMode.Off : AutostartMode.Normal;
        return AutostartMode.Off;
    }

    public static AutostartAction Target(AutostartMode current, AutostartMode selected,
                                          bool normalDisabledInTaskManager, bool adminStale)
    {
        if (selected != current) return SetAction(selected);

        if (selected == AutostartMode.Normal && normalDisabledInTaskManager) return AutostartAction.SetNormal;
        if (selected == AutostartMode.Admin && adminStale) return AutostartAction.SetAdmin;
        return AutostartAction.None;
    }

    public static string ModeName(AutostartMode mode) => mode switch
    {
        AutostartMode.Off => "off",
        AutostartMode.Normal => "normal",
        AutostartMode.Admin => "admin",
        _ => throw new UnreachableException(),
    };

    private static AutostartAction SetAction(AutostartMode mode) => mode switch
    {
        AutostartMode.Off => AutostartAction.SetOff,
        AutostartMode.Normal => AutostartAction.SetNormal,
        AutostartMode.Admin => AutostartAction.SetAdmin,
        _ => throw new UnreachableException(),
    };
}
