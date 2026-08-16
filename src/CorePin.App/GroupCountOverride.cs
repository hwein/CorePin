#if DEBUG
using CorePin.Core.Platform;
using CorePin.Core.Topology;

namespace CorePin.App;

/// --debug-groups: forces ActiveGroupCount for acceptance criterion 25 (S01 §5.6,
/// S04 §7.3). The core records stay those of the real machine, so the message box shows
/// an internally inconsistent number on the development machine — intended, not to be
/// "fixed": criterion 25 checks that the box appears, that the process ends and that
/// config.json is untouched.
internal sealed class GroupCountOverride(ITopologySource inner, int groups) : ITopologySource
{
    public TopologySnapshot Read() => inner.Read() with { ActiveGroupCount = groups };

    /// Pass the inner source's warnings through unchanged (S04 §2.7, T-O4): the decorator
    /// produces none of its own and must swallow none.
    public IReadOnlyList<string> Warnings => inner.Warnings;
}
#endif
