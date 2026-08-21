namespace CorePin.Core.Autostart;

public static class RunKeyCommand
{
    private const string TrayFlag = " --tray";

    public static string Build(string exePath) => $"\"{exePath}\"{TrayFlag}";

    public static string? ParseExePath(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        if (value[0] == '"')
        {
            int closingQuote = value.IndexOf('"', 1);
            return closingQuote < 0 ? null : value[1..closingQuote];
        }

        return value.EndsWith(TrayFlag, StringComparison.OrdinalIgnoreCase)
            ? value[..^TrayFlag.Length].Trim()
            : value.Trim();
    }

    public static bool Matches(string? value, string exePath)
        => string.Equals(value, Build(exePath), StringComparison.OrdinalIgnoreCase);
}
