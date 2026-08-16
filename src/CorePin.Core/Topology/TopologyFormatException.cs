namespace CorePin.Core.Topology;

/// Thrown by TopologyJson.Parse for a malformed dump (S04 §3.6) and by
/// ClusterBuilder.Build for structurally impossible input (S04 §4.2).
public sealed class TopologyFormatException : Exception
{
    public TopologyFormatException(string message) : base(message) { }
}
