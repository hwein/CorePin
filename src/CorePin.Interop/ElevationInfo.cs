using System.Security.Principal;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// Elevation cannot change while the process runs, so it is read exactly once.
public static class ElevationInfo
{
    public static bool IsElevated { get; } = ReadElevation();

    /// True for a split token that is not elevated — the account can raise itself via UAC.
    public static bool CanElevate { get; } = ReadCanElevate();

    private static bool ReadElevation()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool ReadCanElevate()
    {
        using var identity = WindowsIdentity.GetCurrent();

        // This class has no log; an unreadable token counts as "cannot elevate".
        return GetTokenInformation(identity.Token, TokenElevationType, out int elevationType,
                                   sizeof(int), out _)
               && elevationType == TokenElevationTypeLimited;
    }
}
