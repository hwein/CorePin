using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using CorePin.Core.Autostart;
using CorePin.Interop;

namespace CorePin.App;

/// The elevated one-shot behind --autostart-task: one task operation, then the process ends.
internal static class AutostartHelper
{
    internal const int Failed = 6;
    internal const string NoExePath = "The location of CorePin.exe could not be determined.";
    internal const string NoSid = "This Windows account has no security identifier.";
    internal const string TaskBelongsToAnotherUser = "The task belongs to another user account.";

    internal static int Run(StartupOptions opts)
    {
        if (opts.AutostartCommand is not { } command)
            return Fail("Missing or unknown command.");

        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value is not { } userSid)
            return Fail(NoSid);

        try
        {
            var task = new AutostartTask();
            var state = task.Read();
            bool ownTask = AutostartTruth.IsOwnTask(state, userSid);

            if (command == AutostartTaskCommand.Create)
            {
                if (state.Present && !ownTask)
                    return Fail(TaskBelongsToAnotherUser);
                if (Environment.ProcessPath is not { } exePath)
                    return Fail(NoExePath);
                task.Register(exePath, identity.Name, userSid);
            }
            else if (ownTask)
            {
                task.Delete();
            }

            return 0;
        }
        // A leaf process without a log file: every failure has to reach the user as a dialog.
        catch (Exception ex)
        {
            return Fail(DescribeFailure(ex));
        }
    }

    private static int Fail(string detail)
    {
        MessageBoxes.ShowAutostartFailed(detail);
        return Failed;
    }

    /// .NET already appends the HRESULT to these exceptions' Message as a trailing " (0x...)".
    internal static string DescribeFailure(Exception ex)
    {
        if (ex is COMException or UnauthorizedAccessException or FileNotFoundException)
        {
            int suffixStart = ex.Message.IndexOf(" (0x", StringComparison.Ordinal);
            string message = suffixStart < 0 ? ex.Message : ex.Message[..suffixStart];
            return $"Windows reported: 0x{ex.HResult:X8} {message}";
        }

        return $"{ex.GetType().Name}: {ex.Message}";
    }
}
