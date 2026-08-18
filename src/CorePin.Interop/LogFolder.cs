using System.ComponentModel;
using System.Diagnostics;
using CorePin.Core.Diagnostics;

namespace CorePin.Interop;

/// Opens the log directory in the shell; a failure here is a log line, never a crash.
public static class LogFolder
{
    public static bool TryOpen(string? directory, ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (string.IsNullOrEmpty(directory)) return false;

        try
        {
            Directory.CreateDirectory(directory);   // sparse logging may not have created it yet
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException
                                        or FileNotFoundException)
        {
            log.Warning("tray", $"open log folder failed: {ex.Message}");
            return false;
        }
    }
}
