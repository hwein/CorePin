#if DEBUG
using System.IO;
using CorePin.Core.Platform;
using CorePin.Core.Topology;

namespace CorePin.App;

/// --debug-topology: a frozen dump loaded as the CURRENT topology (S01 §5.6, S04 §7.3).
/// Lives in CorePin.App because it is only reachable from the composition root and, like
/// GroupCountOverride, is internal — an internal type in CorePin.Interop would not be
/// visible to Program.Main.
internal sealed class FileTopologySource(string path) : ITopologySource
{
    /// A dump read from a file has no Win32 anomalies, and an invented warning would be
    /// worse than none (S04 §7.3).
    public IReadOnlyList<string> Warnings => [];

    public TopologySnapshot Read()
    {
        try
        {
            return TopologyJson.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is TopologyFormatException or IOException
                                      or UnauthorizedAccessException or ArgumentException)
        {
            // Same path as a Win32 read failure: message box, exit code 4 (S04 T22). The
            // caller only has to know one exception type.
            throw new TopologyReadException($"could not read topology fixture: {ex.Message}", ex);
        }
    }
}
#endif
