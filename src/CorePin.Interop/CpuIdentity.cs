using System.Security;
using Microsoft.Win32;

namespace CorePin.Interop;

/// CPU identity from the registry (02 §4.2, S07 §7.2). Called by Win32TopologySource,
/// never from CorePin.Core.Topology (S01 §3.8).
internal static class CpuIdentity
{
    /// Never null and never untrimmed; ("", "") when the key cannot be read. The empty
    /// string is the only value that claims nothing — it hits no vendor branch and leads
    /// cleanly to "Group n" via G1 (S04 §2.6).
    public static (string Vendor, string CpuName) Read()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", writable: false);
            string vendor = (key?.GetValue("VendorIdentifier") as string)?.Trim() ?? "";
            string name = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
            return (vendor, name);
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return ("", "");
        }
    }
}
