using System.Security.Principal;

namespace CorePin.Interop;

/// Elevation cannot change while the process runs, so it is read exactly once.
public static class ElevationInfo
{
    public static bool IsElevated { get; } = ReadElevation();

    private static bool ReadElevation()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
