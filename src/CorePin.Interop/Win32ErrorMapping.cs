using CorePin.Core.Platform;

namespace CorePin.Interop;

/// Named, not inline: the test runner needs a member it can call directly.
internal static class Win32ErrorMapping
{
    internal static OpenFailure ClassifyOpenFailure(int win32Error) => win32Error switch
    {
        Win32Error.ACCESS_DENIED => OpenFailure.AccessDenied,
        Win32Error.INVALID_PARAMETER => OpenFailure.Gone,
        _ => OpenFailure.Other,
    };
}
