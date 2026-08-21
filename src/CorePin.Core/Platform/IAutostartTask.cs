namespace CorePin.Core.Platform;

public interface IAutostartTask
{
    /// HRESULT 0x80070002 reads as Present = false; other failures throw.
    TaskState Read();

    /// TASK_CREATE_OR_UPDATE: creates the task or replaces an existing one of the same name.
    void Register(string exePath, string userName, string userSid);

    /// HRESULT 0x80070002 is not an error.
    void Delete();
}
