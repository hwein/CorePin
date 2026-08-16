namespace CorePin.Core.Paths;

/// Pure path arithmetic: never touches the file system, never throws, creates nothing.
public sealed class AppPaths
{
    private AppPaths(string root)
    {
        Root = root;
        ConfigDirectory = root;
        ConfigFile = Path.Combine(root, "config.json");
        LogDirectory = Path.Combine(root, "logs");
    }

    /// %LOCALAPPDATA%\CorePin\
    public string Root { get; }

    public string ConfigDirectory { get; }

    public string ConfigFile { get; }

    /// %LOCALAPPDATA%\CorePin\logs\ — the PRIMARY path; FileLog may fall back.
    public string LogDirectory { get; }

    public static AppPaths ForCurrentUser()
    {
        // GetFolderPath returns "" when unresolvable, and Path.Combine tolerates that.
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(Path.Combine(localAppData, "CorePin"));
    }
}
