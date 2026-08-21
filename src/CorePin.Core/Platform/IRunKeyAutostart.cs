namespace CorePin.Core.Platform;

public interface IRunKeyAutostart
{
    RunKeyState Read();

    /// Sets the run key value and clears StartupApproved.
    void Write(string exePath);

    /// Clears both values; a missing value is not an error.
    void Remove();
}
