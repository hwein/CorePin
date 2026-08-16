namespace CorePin.Core.Platform;

public interface IAffinityAccess
{
    /// Opens with PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_INFORMATION.
    ProcessOpenResult Open(int pid);
}
