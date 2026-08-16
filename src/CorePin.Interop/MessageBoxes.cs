using System.Globalization;
using System.Numerics;
using System.Text;
using CorePin.Core.Topology;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// MessageBoxW wrappers for aborts BEFORE any WPF init: modal, without a parent HWND.
public static class MessageBoxes
{
    private const string Caption = "CorePin";

    /// No plural logic: the branch is only reachable with g >= 2, so both numbers are plural.
    private const string GroupLimitText =
        "This system has {0} logical processors across {1} processor groups.\n"
        + "CorePin supports single-group systems (≤ 64 logical processors) only.\n"
        + "Your settings have not been changed.";

    private static readonly CompositeFormat GroupLimitFormat = CompositeFormat.Parse(GroupLimitText);

    public static void ShowGroupLimit(TopologySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Sum of PopCount, not an OR: multi-group masks are group-relative.
        int logicalProcessors = 0;
        foreach (var core in snapshot.Cores) logicalProcessors += BitOperations.PopCount(core.Mask);

        string text = string.Format(CultureInfo.InvariantCulture, GroupLimitFormat,
                                    logicalProcessors, snapshot.ActiveGroupCount);
        MessageBox(0, text, Caption, MB_OK | MB_ICONWARNING);
    }

    /// Nothing is broken, CorePin just cannot work — hence the information symbol.
    public static void ShowTopologyReadFailed(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        string text = "CorePin could not read the CPU topology and cannot continue.\n\n"
                    + ex.Message;
        MessageBox(0, text, Caption, MB_OK | MB_ICONINFORMATION);
    }

#if DEBUG
    public static void ShowFixtureTooLarge(int fixtureLp, int machineLp)
    {
        string text = string.Create(CultureInfo.InvariantCulture,
            $"The topology fixture reports {fixtureLp} logical processors, this machine has {machineLp}.\n--debug-topology was rejected; nothing has been changed.");
        MessageBox(0, text, Caption, MB_OK | MB_ICONWARNING);
    }
#endif
}
