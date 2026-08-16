namespace CorePin.Core.Paths;

/// Pure path arithmetic (S01 §2.4). Never touches the file system, never throws:
/// it runs before the logger exists, so a failure here would be unloggable.
/// Creating the directories belongs to the classes that write there.
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

    /// Directory holding config.json (S05 §2.1).
    public string ConfigDirectory { get; }

    public string ConfigFile { get; }

    /// %LOCALAPPDATA%\CorePin\logs\ — the PRIMARY path; FileLog may fall back (S02 §3.1).
    public string LogDirectory { get; }

    public static AppPaths ForCurrentUser()
    {
        // GetFolderPath returns "" when the folder cannot be resolved; Path.Combine
        // tolerates that, so no branch and no throw is needed here.
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(Path.Combine(localAppData, "CorePin"));
    }
}
