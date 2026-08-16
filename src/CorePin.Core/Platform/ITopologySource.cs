using CorePin.Core.Topology;

namespace CorePin.Core.Platform;

public interface ITopologySource
{
    /// Throws TopologyReadException when the topology cannot be read.
    TopologySnapshot Read();

    /// Anomalies of the last Read() as DATA (S01 §3.3, T-O4). The source is created in
    /// S01 §3.7 step 0, the logger only in step 3 — it cannot log. Content, wording and
    /// order are fixed by S04 §2.7.
    IReadOnlyList<string> Warnings { get; }
}
