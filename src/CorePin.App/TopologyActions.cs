using System.Runtime.InteropServices;
using System.Windows;
using CorePin.Core.Diagnostics;
using CorePin.Core.Topology;

namespace CorePin.App;

/// The clipboard needs STA, which only the app layer has.
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
}
