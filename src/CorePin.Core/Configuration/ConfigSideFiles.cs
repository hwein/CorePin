using System.Globalization;
using System.Text;
using System.Text.Json;
using CorePin.Core.Diagnostics;
using CorePin.Core.Time;

namespace CorePin.Core.Configuration;

/// The two files written past the write guard, both best effort: a failure changes no outcome.
internal sealed class ConfigSideFiles(string directory, ILog log, IClock clock)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// Returns the name the file was renamed to, or null if the rename failed (old name kept).
    public string? RenameCorrupt(string path)
    {
        string name = FreeCorruptName();
        try { File.Move(path, Path.Combine(directory, name)); }
        catch (Exception) { return null; }
        return name;
    }

    /// The only file ConfigStore deletes on its own.
    public string? UpdateSkipped(List<JsonElement> skippedRaw)
    {
        string path = Path.Combine(directory, ConfigFileNames.Skipped);

        if (skippedRaw.Count == 0)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    log.Debug("config", "config.skipped.json removed, no rules skipped on this load");
                }
            }
            catch (Exception)
            {
                // Best effort: a leftover diagnostic file is not worth a failure.
            }
            return null;
        }

        try
        {
            string json = JsonSerializer.Serialize(skippedRaw.ToArray(), ConfigJsonContext.Default.JsonElementArray);
            File.WriteAllText(path, json, Utf8NoBom);
        }
        catch (Exception)
        {
            // RulesSkipped and Outcome stay correct, only the diagnostic file is missing.
        }
        return ConfigFileNames.Skipped;
    }

    private string FreeCorruptName()
    {
        // Local time: a user looking at the file places it faster.
        string stamp = clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string candidate = $"config.corrupt-{stamp}.json";
        for (int suffix = 2; File.Exists(Path.Combine(directory, candidate)); suffix++)
            candidate = $"config.corrupt-{stamp}-{suffix}.json";
        return candidate;
    }
}
