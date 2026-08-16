using System.IO;
using CorePin.Core.Diagnostics;
using CorePin.Core.Platform;
using CorePin.Core.Topology;
using CorePin.Interop;

namespace CorePin.App;

/// The WPF-free path for --dump-topology (S04 §8.2). No UI, no config, no log file, no
/// mutex, and no abort on more than one processor group — the dumper is a diagnostic
/// tool, and a two-group system is exactly the one whose dump is interesting.
internal static class DumpTopologyCommand
{
    /// The log parameter is always NullLog.Instance here (S01 §3.7); it is part of the
    /// signature S01 fixes, and it is deliberately unused — that IS the promise of §8.3.
    internal static int Run(ITopologySource source, StartupOptions opts, ILog log)
    {
        TopologySnapshot snapshot;
        try { snapshot = source.Read(); }
        catch (Exception) { return 4; }     // also TopologyFormatException from FileTopologySource

        string text = TopologyJson.Serialize(snapshot);

        if (!ConsoleAttach.TryWrite(text, out _)) return 2;

#if DEBUG
        if (opts.DebugDumpRaw)
        {
            // Only meaningful through Win32TopologySource: behind a fixture or a decorator
            // there is no raw buffer. Not guessed and not ignored — the run ends with 2,
            // like any other unfulfillable output wish (S04 §8.4).
            if (source is not Win32TopologySource win32) return 2;
            if (!TryWriteRawBuffer(win32)) return 2;
        }
#endif
        return 0;
    }

#if DEBUG
    /// Base64 of the raw buffer, one line, LF at the end, into the WORKING DIRECTORY —
    /// never to stdout, which carries the JSON that criterion 13 compares byte for byte.
    private static bool TryWriteRawBuffer(Win32TopologySource source)
    {
        try
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "topology.raw.b64");
            File.WriteAllBytes(path,
                System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(source.ReadRawBuffer()) + "\n"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or TopologyReadException)
        {
            return false;
        }
    }
#endif
}
