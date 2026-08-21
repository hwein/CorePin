using System.Runtime.InteropServices;
using CorePin.Core.Autostart;
using CorePin.Core.Platform;

namespace CorePin.Interop;

/// Task Scheduler over late-bound IDispatch: six calls do not warrant hand-written vtables.
/// https://learn.microsoft.com/windows/win32/taskschd/task-scheduler-start-page
public sealed class AutostartTask : IAutostartTask
{
    private const string TaskName = "CorePin Autostart";
    private const string RootFolderPath = "\\";

    /// GetTask and DeleteTask report a missing task as ERROR_FILE_NOT_FOUND, wrapped in an HRESULT.
    private const int TaskNotFound = unchecked((int)0x80070002);

    private const int RegDbClassNotRegistered = unchecked((int)0x80040154);

    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;

    private readonly dynamic _service;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2201:Do not raise reserved exception types",
        Justification = "Callers sort Task Scheduler failures by exception type and HResult; a "
                      + "missing ProgID has to arrive as the same COMException as any other one.")]
    public AutostartTask()
    {
        var progId = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new COMException("Schedule.Service is not registered on this computer.",
                                      RegDbClassNotRegistered);
        _service = Activator.CreateInstance(progId)
            ?? throw new COMException("Schedule.Service could not be created.",
                                      RegDbClassNotRegistered);
        _service.Connect();
    }

    public TaskState Read()
    {
        dynamic task;
        try
        {
            task = RootFolder.GetTask(TaskName);
        }
        catch (Exception ex) when (ex.HResult == TaskNotFound)
        {
            return new TaskState(false, null, null, false);
        }

        string xml = task.Xml;
        var content = AutostartTaskXml.Read(xml);

        string? sid = content?.PrincipalSid;
        bool enabled = task.Enabled;

        return new TaskState(true, content?.ExePath, sid, enabled);
    }

    public void Register(string exePath, string userName, string userSid)
    {
        RootFolder.RegisterTask(TaskName, AutostartTaskXml.Build(exePath, userName, userSid),
                                TaskCreateOrUpdate, userSid, null,
                                TaskLogonInteractiveToken, null);
    }

    public void Delete()
    {
        try
        {
            RootFolder.DeleteTask(TaskName, 0);
        }
        catch (Exception ex) when (ex.HResult == TaskNotFound)
        {
        }
    }

    private dynamic RootFolder => _service.GetFolder(RootFolderPath);
}
