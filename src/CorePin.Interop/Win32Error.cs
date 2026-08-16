namespace CorePin.Interop;

/// Every Win32 error-code constant CorePin uses stands here and nowhere else.
internal static class Win32Error
{
    internal const int ACCESS_DENIED = 5;
    internal const int INVALID_HANDLE = 6;
    internal const int INVALID_PARAMETER = 87;
    internal const int INSUFFICIENT_BUFFER = 122;
}
