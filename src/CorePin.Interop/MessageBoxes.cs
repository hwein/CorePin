using System.Globalization;
using System.Numerics;
using System.Text;
using CorePin.Core.Topology;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// MessageBoxW wrappers for the aborts that happen BEFORE any WPF initialisation
/// (S01 §3.7/§3.9). All modal, all without a parent HWND.
///
/// ShowSecondInstanceUnreachable (S01 §3.9) is missing on purpose: single instance
/// belongs to S08, so in phase 1 it would have no caller.
public static class MessageBoxes
{
    private const string Caption = "CorePin";

    /// 02 §4.5, verbatim. Two placeholders, no formatting, no wrapping, no plural logic —
    /// the branch is only reachable with g >= 2 and both numbers are plural (S04 §7.2).
    private const string GroupLimitText =
        "This system has {0} logical processors across {1} processor groups.\n"
        + "CorePin supports single-group systems (≤ 64 logical processors) only.\n"
        + "Your settings have not been changed.";

    private static readonly CompositeFormat GroupLimitFormat = CompositeFormat.Parse(GroupLimitText);

    public static void ShowGroupLimit(TopologySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Sum of PopCount per record, not an OR over the masks: on a multi-group system
        // the masks are group-relative, so an OR would be wrong (S04 §7.2).
        int logicalProcessors = 0;
        foreach (var core in snapshot.Cores) logicalProcessors += BitOperations.PopCount(core.Mask);

        string text = string.Format(CultureInfo.InvariantCulture, GroupLimitFormat,
                                    logicalProcessors, snapshot.ActiveGroupCount);
        MessageBox(0, text, Caption, MB_OK | MB_ICONWARNING);
    }

    /// Nothing is broken, CorePin just cannot work — hence the information symbol
    /// (S01 §3.9, 01 P4).
    public static void ShowTopologyReadFailed(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        // Wording verbatim from S08 §9.2 — the only spec that carries this string.
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
