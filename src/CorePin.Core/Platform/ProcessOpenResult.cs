namespace CorePin.Core.Platform;

public readonly struct ProcessOpenResult
{
    public ProcessOpenResult(IProcessHandle? handle, OpenFailure failure, int win32Error)
    {
        Handle = handle;
        Failure = failure;
        Win32Error = win32Error;
    }

    /// null iff Failure != None.
    public IProcessHandle? Handle { get; }

    public OpenFailure Failure { get; }

    public int Win32Error { get; }
}
