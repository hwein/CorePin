using CorePin.Core.Topology;

namespace CorePin.Core.Platform;

public interface ITopologySource
{
    /// Throws TopologyReadException when the topology cannot be read.
    TopologySnapshot Read();

    /// Anomalies of the last Read() as DATA — the source exists before the logger does.
    IReadOnlyList<string> Warnings { get; }
}
