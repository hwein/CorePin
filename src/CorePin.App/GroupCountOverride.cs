#if DEBUG
using CorePin.Core.Platform;
using CorePin.Core.Topology;

namespace CorePin.App;

/// --debug-groups forces ActiveGroupCount; the resulting inconsistent count is intended.
internal sealed class GroupCountOverride(ITopologySource inner, int groups) : ITopologySource
{
    public TopologySnapshot Read() => inner.Read() with { ActiveGroupCount = groups };

    public IReadOnlyList<string> Warnings => inner.Warnings;
}
#endif
