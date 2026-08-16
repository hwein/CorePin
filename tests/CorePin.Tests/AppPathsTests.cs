using CorePin.Core.Paths;

namespace CorePin.Tests;

public static class AppPathsTests
{
    /// What this does NOT prove: that AppPaths creates no directory. That would need a
    /// machine on which %LOCALAPPDATA%\CorePin\logs does not exist yet, and on a developer
    /// machine it always does — the check would compare true with true. Provable without
    /// an injection layer is only that the paths are pure Path.Combine arithmetic over
    /// Environment.GetFolderPath and that no call throws (S01 §2.4, S05 Ä-5).
    public static void Test_ComposesPathsFromLocalAppData()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var paths = AppPaths.ForCurrentUser();

        Assert.Equal(Path.Combine(localAppData, "CorePin"), paths.Root, "root is %LOCALAPPDATA%\\CorePin");
        Assert.Equal(Path.Combine(localAppData, "CorePin", "logs"), paths.LogDirectory,
            "log directory is composed, not resolved through the file system");
    }

    public static void Test_NeverThrows()
    {
        var paths = AppPaths.ForCurrentUser();

        Assert.True(paths.Root.Length > 0, "Root is set");
        Assert.Equal(paths.Root, paths.ConfigDirectory, "config.json sits directly below CorePin\\");
        Assert.Equal(Path.Combine(paths.Root, "logs"), paths.LogDirectory, "logs\\ sits below CorePin\\");
        Assert.Equal(Path.Combine(paths.Root, "config.json"), paths.ConfigFile, "file name config.json");
        Assert.True(paths.Root.EndsWith("CorePin", StringComparison.Ordinal), "root ends in CorePin");
    }
}
