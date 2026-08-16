using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CorePin.Core.Diagnostics;

namespace CorePin.Core.Configuration;

/// Writes config.json or nothing: every failure ends in a warn line, none in an exception.
internal sealed class ConfigFileWriter(string directory, ILog log)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// No Encoder in [JsonSourceGenerationOptions]; the options overload warns IL2026/IL3050.
    private static readonly ConfigJsonContext WriteContext =
        new(new JsonSerializerOptions(ConfigJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    public void Write(AppConfig config)
    {
        string json;
        try
        {
            json = Serialize(config);
        }
        catch (Exception ex)
        {
            // Serialization sits INSIDE the try: Save() must never throw. Not retryable.
            log.Warn("config", $"write failed: {ex.GetType().Name} {ex.HResult}");
            return;
        }

        ReadOnlySpan<int> backoffMs = [50, 150, 400];
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(directory);
                WriteAtomically(json);
                return;
            }
            catch (IOException ex) when (attempt < backoffMs.Length)
            {
                log.Warn("config",
                    $"write attempt {attempt + 1} failed: {ex.GetType().Name} {ex.HResult}, retrying");
                Thread.Sleep(backoffMs[attempt]);
            }
            catch (Exception ex)
            {
                // Catch-all so Save never throws; no ex.Message — it can carry the user name.
                log.Warn("config", $"write failed: {ex.GetType().Name} {ex.HResult}");
                return;
            }
        }
    }

    /// The temp file must sit in the SAME directory as the target.
    private void WriteAtomically(string json)
    {
        string temp = Path.Combine(directory, ConfigFileNames.Temp);
        string target = Path.Combine(directory, ConfigFileNames.Config);

        // FileMode.Create silently overwrites an orphaned .tmp from an earlier run.
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Utf8NoBom.GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);                 // calls FlushFileBuffers
        }

        // File.Replace throws FileNotFoundException when the target does not exist yet.
        if (File.Exists(target)) File.Replace(temp, target, destinationBackupFileName: null);
        else File.Move(temp, target);
    }

    private static string Serialize(AppConfig config)
    {
        var dto = new ConfigFileWriteDto
        {
            SchemaVersion = config.SchemaVersion,
            Machine = new MachineWriteDto
            {
                CpuName = config.Machine.CpuName,
                LogicalProcessors = config.Machine.LogicalProcessors,
            },
            Settings = new SettingsWriteDto
            {
                PollIntervalMs = config.Settings.PollIntervalMs,
                StartWithWindows = config.Settings.StartWithWindows,
                WindowBounds = ToJson(config.Settings.WindowBounds),
                LogLevel = config.Settings.LogLevel.ToString().ToLowerInvariant(),
            },
            Rules = [.. config.Rules.Rules.Select(r => new RuleWriteDto
            {
                Id = r.Id.ToString(),
                ExeName = r.ExeName,
                LastKnownPath = r.LastKnownPath,
                Threads = [.. r.Threads.ToThreads()],
                Enabled = r.Enabled,
            })],
        };
        // Trailing "\n": a file without a final line ending is the exception everywhere.
        return JsonSerializer.Serialize(dto, WriteContext.ConfigFileWriteDto) + "\n";
    }

    private static WindowBoundsJson? ToJson(WindowBounds? bounds)
        => bounds is null ? null : new WindowBoundsJson { X = bounds.X, Y = bounds.Y, W = bounds.W, H = bounds.H };
}
