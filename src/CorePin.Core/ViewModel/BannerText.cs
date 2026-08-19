namespace CorePin.Core.ViewModel;

/// The five banner cases and the tooltip a locked control repeats.
public static class BannerText
{
    public const string CpuChanged = "CPU changed — your core selections need a review.";

    public const string ConfigTooNew =
        "This config was written by a newer version of CorePin — update CorePin or move the file away.";

    public const string ConfigUnreadable =
        "CorePin couldn't read config.json — starting empty. Changes in this session won't be saved.";

    public const string EditingStopped =
        "Monitoring stopped — quit from the tray icon and start CorePin again to edit rules.";

    /// The rename may itself have failed; then the name is left out instead of invented.
    public static string Corrupt(string? renamedTo)
        => renamedTo is null
            ? "config.json couldn't be read and was reset."
            : $"config.json couldn't be read and was reset — your previous file was saved as {renamedTo}.";

    public static string RulesSkipped(int count, string? detail)
        => detail is null
            ? $"{count} rule(s) could not be loaded and were skipped."
            : $"{count} rule(s) could not be loaded and were skipped — see {detail} in the CorePin folder.";
}
