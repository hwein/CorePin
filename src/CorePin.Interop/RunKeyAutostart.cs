using Microsoft.Win32;
using CorePin.Core.Autostart;
using CorePin.Core.Platform;

namespace CorePin.Interop;

/// Registry failures pass through: the caller logs them and shows the dialog.
public sealed class RunKeyAutostart : IRunKeyAutostart
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ApprovedPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ValueName = "CorePin";

    public RunKeyState Read()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunPath);
        // A value of a foreign type is no command line, so it reads as no value at all.
        string? value = run?.GetValue(ValueName) as string;

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedPath);
        return new RunKeyState(value, StartupApproved.IsDisabled(approved?.GetValue(ValueName) as byte[]));
    }

    public void Write(string exePath)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunPath, writable: true))
            run.SetValue(ValueName, RunKeyCommand.Build(exePath), RegistryValueKind.String);

        RemoveApproval();
    }

    public void Remove()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunPath, writable: true))
            run?.DeleteValue(ValueName, throwOnMissingValue: false);

        RemoveApproval();
    }

    /// A missing StartupApproved value means enabled, so deleting it is how Normal is switched on.
    private static void RemoveApproval()
    {
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedPath, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
