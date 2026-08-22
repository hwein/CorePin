using System.IO;
using System.Runtime.InteropServices;

namespace CorePin.App;

/// The detail line of an autostart change that did not happen — it reaches both dialog and log.
internal static class AutostartTexts
{
    internal const string NoExePath = "The location of CorePin.exe could not be determined.";
    internal const string NoSid = "This Windows account has no security identifier.";
    internal const string TaskBelongsToAnotherUser = "The task belongs to another user account.";

    internal const string NeedsAdmin =
        "Start with Windows was turned on from an instance running as administrator. "
        + "Start CorePin as administrator to turn it off.";

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
