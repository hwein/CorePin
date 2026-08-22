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

    public static void Test_Resolve_RunKeyPresent_OwnTaskDisabled_IsNormal()
    {
        Assert.Equal(AutostartMode.Normal, AutostartTruth.Resolve(RunKeyPresent, OwnTaskDisabled, OwnSid),
            "a disabled task starts nothing, so the run key next to it decides");
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

    public static void Test_Switch_Off_NotElevated_TurnsOnRunKey()
    {
        Assert.Equal(AutostartSwitch.TurnOnRunKey,
            AutostartTruth.Switch(AutostartMode.Off, isElevated: false),
            "a normal instance can only ever write the run key");
    }

    public static void Test_Switch_Off_Elevated_TurnsOnTask()
    {
        Assert.Equal(AutostartSwitch.TurnOnTask,
            AutostartTruth.Switch(AutostartMode.Off, isElevated: true),
            "an elevated instance registers the task, so the autostart keeps its rights");
    }

    public static void Test_Switch_Normal_NotElevated_TurnsOff()
    {
        Assert.Equal(AutostartSwitch.TurnOff,
            AutostartTruth.Switch(AutostartMode.Normal, isElevated: false),
            "the run key is the normal instance's own to remove");
    }

    public static void Test_Switch_Normal_Elevated_TurnsOff()
    {
        Assert.Equal(AutostartSwitch.TurnOff,
            AutostartTruth.Switch(AutostartMode.Normal, isElevated: true),
            "an elevated instance removes the run key as well");
    }

    public static void Test_Switch_Admin_NotElevated_RefusesNeedsAdmin()
    {
        Assert.Equal(AutostartSwitch.RefuseNeedsAdmin,
            AutostartTruth.Switch(AutostartMode.Admin, isElevated: false),
            "deleting the task needs rights this instance does not have and never asks for");
    }

    public static void Test_Switch_Admin_Elevated_TurnsOff()
    {
        Assert.Equal(AutostartSwitch.TurnOff,
            AutostartTruth.Switch(AutostartMode.Admin, isElevated: true),
            "the instance that could create the task can also delete it");
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

    public static void Test_IsOwnTask()
    {
        Assert.Equal(true, AutostartTruth.IsOwnTask(OwnTaskEnabled, OwnSid), "matching principal SID is the own task");
        Assert.Equal(false, AutostartTruth.IsOwnTask(ForeignTask, OwnSid), "a different principal SID is not the own task");
        Assert.Equal(false, AutostartTruth.IsOwnTask(TaskAbsent, OwnSid), "no task present is not the own task");
        Assert.Equal(false, AutostartTruth.IsOwnTask(new TaskState(true, ExePath, null, true), OwnSid), "a null principal SID is not the own task");
    }
}
