using System.Security;
using Microsoft.Win32;

namespace CorePin.Interop;

/// CPU identity from the registry — never called from CorePin.Core.Topology.
internal static class CpuIdentity
{
    /// ("", "") when unreadable — the empty string claims nothing and hits no vendor branch.
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
