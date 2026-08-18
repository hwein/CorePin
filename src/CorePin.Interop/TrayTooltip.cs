namespace CorePin.Interop;

/// The tooltip text of the tray icon; the plural s drops at exactly one rule, not at zero.
public static class TrayTooltip
{
    public static string Format(int totalRules, int appliedRules) =>
        $"CorePin — {totalRules} rule{(totalRules == 1 ? "" : "s")}, {appliedRules} active";
}
