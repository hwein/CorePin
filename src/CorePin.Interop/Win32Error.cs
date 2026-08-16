namespace CorePin.Interop;

/// The one canonical error-code file (S07 §14.2): every error-code constant that a
/// CorePin native call can return stands here and nowhere else.
internal static class Win32Error
{
    internal const int ACCESS_DENIED = 5;
    internal const int INVALID_HANDLE = 6;
    internal const int INVALID_PARAMETER = 87;
    internal const int INSUFFICIENT_BUFFER = 122;
}
