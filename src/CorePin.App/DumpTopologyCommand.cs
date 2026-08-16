using System.IO;
using CorePin.Core.Diagnostics;
using CorePin.Core.Platform;
using CorePin.Core.Topology;
using CorePin.Interop;

namespace CorePin.App;

/// The WPF-free path for --dump-topology: no UI, no config, no log, no group-limit abort.
internal static class DumpTopologyCommand
{
    /// The log parameter is deliberately unused — --dump-topology writes no log.
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
            // Behind a fixture or a decorator there is no raw buffer; the run ends with 2.
            if (source is not Win32TopologySource win32) return 2;
            if (!TryWriteRawBuffer(win32)) return 2;
        }
#endif
        return 0;
    }

#if DEBUG
    /// Into the WORKING DIRECTORY, never to stdout — stdout carries the dump JSON.
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
