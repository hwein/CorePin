using System.Text;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// A WinExe gets no console from the loader, but --dump-topology has to write to stdout.
public static class ConsoleAttach
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// UTF-8 without BOM. No AllocConsole (it would pop a window) and no message box.
    public static bool TryWrite(string text, out string destination)
    {
        byte[] bytes = Utf8NoBom.GetBytes(text);

        nint handle = GetStdHandle(STD_OUTPUT_HANDLE);
        if (handle != 0 && handle != -1)
        {
            try
            {
                using var stdout = Console.OpenStandardOutput();
                stdout.Write(bytes, 0, bytes.Length);
                stdout.Flush();
                destination = "stdout";
                return true;
            }
            catch (IOException) { /* fall through to the next step */ }
            catch (NotSupportedException) { }
        }

        if (AttachConsole(ATTACH_PARENT_PROCESS))
        {
            try
            {
                using var console = File.Open("CONOUT$", FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                console.Write(bytes, 0, bytes.Length);
                console.Flush();
                destination = "CONOUT$";
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        try
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "topology.json");
            File.WriteAllBytes(path, bytes);
            destination = path;
            return true;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        destination = "";
        return false;
    }
}
