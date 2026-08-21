using CorePin.Core.Autostart;
using CorePin.Core.Platform;

namespace CorePin.Tests;

public static class AutostartTruthTests
{
    private const string OwnSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string ForeignSid = "S-1-5-21-9999999999-8888888888-7777777777-1002";
    private const string ExePath = @"C:\Program Files\CorePin\CorePin.exe";

    private static readonly RunKeyState RunKeyAbsent = new(null, false);
    private static readonly RunKeyState RunKeyPresent = new("present", false);
    private static readonly RunKeyState RunKeyDisabled = new("present", true);

    private static readonly TaskState TaskAbsent = new(false, null, null, false);
    private static readonly TaskState OwnTaskEnabled = new(true, ExePath, OwnSid, true);
    private static readonly TaskState OwnTaskDisabled = new(true, ExePath, OwnSid, false);
    private static readonly TaskState ForeignTask = new(true, ExePath, ForeignSid, true);

    public static void Test_Resolve_RunKeyAbsent_TaskAbsent_IsOff()
    {
        Assert.Equal(AutostartMode.Off, AutostartTruth.Resolve(RunKeyAbsent, TaskAbsent, OwnSid),
            "no run key and no task means autostart was never set up");
    }

    public static void Test_Resolve_RunKeyPresent_TaskAbsent_IsNormal()
    {
        Assert.Equal(AutostartMode.Normal, AutostartTruth.Resolve(RunKeyPresent, TaskAbsent, OwnSid),
            "an enabled run key with no task is the Normal mechanism");
    }

    public static void Test_Resolve_RunKeyDisabled_TaskAbsent_IsOff()
    {
        Assert.Equal(AutostartMode.Off, AutostartTruth.Resolve(RunKeyDisabled, TaskAbsent, OwnSid),
            "a run key disabled in Task Manager counts as Off");
    }

    public static void Test_Resolve_RunKeyAbsent_OwnTaskEnabled_IsAdmin()
    {
        Assert.Equal(AutostartMode.Admin, AutostartTruth.Resolve(RunKeyAbsent, OwnTaskEnabled, OwnSid),
            "an enabled task owned by this account is the Admin mechanism");
    }

    public static void Test_Resolve_RunKeyPresent_OwnTaskEnabled_IsAdmin()
    {
        Assert.Equal(AutostartMode.Admin, AutostartTruth.Resolve(RunKeyPresent, OwnTaskEnabled, OwnSid),
            "the own enabled task wins over a leftover run key");
    }

    public static void Test_Resolve_RunKeyDisabled_OwnTaskEnabled_IsAdmin()
    {
        Assert.Equal(AutostartMode.Admin, AutostartTruth.Resolve(RunKeyDisabled, OwnTaskEnabled, OwnSid),
            "the own enabled task wins regardless of the run key's state");
    }

    public static void Test_Resolve_RunKeyAbsent_OwnTaskDisabled_IsOff()
    {
        Assert.Equal(AutostartMode.Off, AutostartTruth.Resolve(RunKeyAbsent, OwnTaskDisabled, OwnSid),
            "the user disabled the task in Task Scheduler");
    }

    public static void Test_Resolve_RunKeyPresent_OwnTaskDisabled_IsOff()
    {
        Assert.Equal(AutostartMode.Off, AutostartTruth.Resolve(RunKeyPresent, OwnTaskDisabled, OwnSid),
            "a disabled own task does not fall back to the run key");
    }

    public static void Test_Resolve_RunKeyDisabled_OwnTaskDisabled_IsOff()
    {
        Assert.Equal(AutostartMode.Off, AutostartTruth.Resolve(RunKeyDisabled, OwnTaskDisabled, OwnSid),
            "both mechanisms agree on Off");
    }

    public static void Test_Resolve_RunKeyAbsent_ForeignTask_IsOff()
    {
        Assert.Equal(AutostartMode.Off, AutostartTruth.Resolve(RunKeyAbsent, ForeignTask, OwnSid),
            "a task belonging to another account counts as not present");
    }

    public static void Test_Resolve_RunKeyPresent_ForeignTask_IsNormal()
    {
        Assert.Equal(AutostartMode.Normal, AutostartTruth.Resolve(RunKeyPresent, ForeignTask, OwnSid),
            "the foreign task is ignored, so the run key decides");
    }

    public static void Test_Resolve_RunKeyDisabled_ForeignTask_IsOff()
    {
        Assert.Equal(AutostartMode.Off, AutostartTruth.Resolve(RunKeyDisabled, ForeignTask, OwnSid),
            "the foreign task is ignored, and the disabled run key means Off");
    }

    public static void Test_Target_OffToOff_IsNone()
    {
        Assert.Equal(AutostartAction.None,
            AutostartTruth.Target(AutostartMode.Off, AutostartMode.Off, normalDisabledInTaskManager: false, adminStale: false),
            "selecting the current mode does nothing");
    }

    public static void Test_Target_OffToNormal_IsSetNormal()
    {
        Assert.Equal(AutostartAction.SetNormal,
            AutostartTruth.Target(AutostartMode.Off, AutostartMode.Normal, normalDisabledInTaskManager: false, adminStale: false),
            "a different selection always acts");
    }

    public static void Test_Target_OffToAdmin_IsSetAdmin()
    {
        Assert.Equal(AutostartAction.SetAdmin,
            AutostartTruth.Target(AutostartMode.Off, AutostartMode.Admin, normalDisabledInTaskManager: false, adminStale: false),
            "a different selection always acts");
    }

    public static void Test_Target_NormalToOff_IsSetOff()
    {
        Assert.Equal(AutostartAction.SetOff,
            AutostartTruth.Target(AutostartMode.Normal, AutostartMode.Off, normalDisabledInTaskManager: false, adminStale: false),
            "a different selection always acts");
    }

    public static void Test_Target_NormalToNormal_IsNone()
    {
        Assert.Equal(AutostartAction.None,
            AutostartTruth.Target(AutostartMode.Normal, AutostartMode.Normal, normalDisabledInTaskManager: false, adminStale: false),
            "the same selection with no exception flag does nothing");
    }

    public static void Test_Target_NormalToAdmin_IsSetAdmin()
    {
        Assert.Equal(AutostartAction.SetAdmin,
            AutostartTruth.Target(AutostartMode.Normal, AutostartMode.Admin, normalDisabledInTaskManager: false, adminStale: false),
            "a different selection always acts");
    }

    public static void Test_Target_AdminToOff_IsSetOff()
    {
        Assert.Equal(AutostartAction.SetOff,
            AutostartTruth.Target(AutostartMode.Admin, AutostartMode.Off, normalDisabledInTaskManager: false, adminStale: false),
            "a different selection always acts");
    }

    public static void Test_Target_AdminToNormal_IsSetNormal()
    {
        Assert.Equal(AutostartAction.SetNormal,
            AutostartTruth.Target(AutostartMode.Admin, AutostartMode.Normal, normalDisabledInTaskManager: false, adminStale: false),
            "a different selection always acts");
    }

    public static void Test_Target_AdminToAdmin_IsNone()
    {
        Assert.Equal(AutostartAction.None,
            AutostartTruth.Target(AutostartMode.Admin, AutostartMode.Admin, normalDisabledInTaskManager: false, adminStale: false),
            "the same selection with no exception flag does nothing");
    }

    public static void Test_Target_NormalToNormal_DisabledInTaskManager_IsSetNormal()
    {
        Assert.Equal(AutostartAction.SetNormal,
            AutostartTruth.Target(AutostartMode.Normal, AutostartMode.Normal, normalDisabledInTaskManager: true, adminStale: false),
            "re-selecting Normal while Task Manager turned it off re-enables it");
    }

    public static void Test_Target_AdminToAdmin_Stale_IsSetAdmin()
    {
        Assert.Equal(AutostartAction.SetAdmin,
            AutostartTruth.Target(AutostartMode.Admin, AutostartMode.Admin, normalDisabledInTaskManager: false, adminStale: true),
            "re-selecting Admin while the task points at a stale path repairs it");
    }

    public static void Test_ModeName_Off()
    {
        Assert.Equal("off", AutostartTruth.ModeName(AutostartMode.Off), "the config.json and log vocabulary for Off");
    }

    public static void Test_ModeName_Normal()
    {
        Assert.Equal("normal", AutostartTruth.ModeName(AutostartMode.Normal), "the config.json and log vocabulary for Normal");
    }

    public static void Test_ModeName_Admin()
    {
        Assert.Equal("admin", AutostartTruth.ModeName(AutostartMode.Admin), "the config.json and log vocabulary for Admin");
    }
}
