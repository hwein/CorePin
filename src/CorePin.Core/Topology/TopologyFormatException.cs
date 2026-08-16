namespace CorePin.Core.Topology;

/// A malformed dump, or structurally impossible input to the cluster building.
public sealed class TopologyFormatException : Exception
{
    public TopologyFormatException(string message) : base(message) { }
}
