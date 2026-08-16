namespace CorePin.Core.Engine;

/// Symbolic names for the Win32 codes the engine's four process calls can return.
internal static class Win32ErrorNames
{
    internal static string Of(int win32Error) => win32Error switch
    {
        0 => "SUCCESS",
        5 => "ACCESS_DENIED",
        6 => "INVALID_HANDLE",
        87 => "INVALID_PARAMETER",
        _ => "UNKNOWN",
    };
}
