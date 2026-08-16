using System.Text;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// A WinExe gets no console from the loader, and 02 §4.6 documents
/// `CorePin.exe --dump-topology > topology.json` as the call form (S01 §6.4).
public static class ConsoleAttach
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// Writes the text as UTF-8 without BOM, in this order: redirected stdout, else a
    /// console attached to the parent process, else topology.json in the working
    /// directory. No AllocConsole (a popping console window is what step 2 avoids) and
    /// no message box (02 §4.6: "no UI").
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
