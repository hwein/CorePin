using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using CorePin.Core.Diagnostics;
using CorePin.Core.Topology;

namespace CorePin.App;

/// Clipboard and browser hand-off; the clipboard needs STA, which only the app layer has.
internal static class TopologyActions
{
    private const int Attempts = 5;
    private const int RetryDelayMs = 100;

    /// Serialised afresh, never a file's content: LF endings, byte-identical to the dump.
    internal static void CopyTopology(TopologySnapshot source, ILog log)
        => TryCopyText(TopologyJson.Serialize(source), log);

    internal static bool TryCopyText(string text, ILog log)
    {
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                if (attempt == Attempts)
                {
                    log.Warning("tray", $"clipboard copy failed after 5 attempts: {ex.Message}");
                    return false;
                }
                Thread.Sleep(RetryDelayMs);
            }
        }
        return false;
    }

    internal static bool TryOpenIssuePage(string cpuName, ILog log)
    {
        string title = string.IsNullOrWhiteSpace(cpuName) ? "CPU profile" : $"CPU profile: {cpuName}";
        string body = "CorePin has copied your topology dump to the clipboard.\n\n" +
                      "Please paste it here, replacing this text.";
        string url = "https://github.com/hwein/CorePin/issues/new" +
                     $"?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            log.Warning("app", $"could not open the issue page in a browser: {ex.Message}");
            return false;
        }
    }
}
