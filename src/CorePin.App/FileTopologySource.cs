#if DEBUG
using System.IO;
using CorePin.Core.Platform;
using CorePin.Core.Topology;

namespace CorePin.App;

/// --debug-topology: a frozen dump loaded as the CURRENT topology.
internal sealed class FileTopologySource(string path) : ITopologySource
{
    /// A dump read from a file has no Win32 anomalies; an invented warning would be worse.
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
            // The caller only has to know one exception type.
            throw new TopologyReadException($"could not read topology fixture: {ex.Message}", ex);
        }
    }
}
#endif
