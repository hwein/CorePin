namespace CorePin.Core.Autostart;

public static class StartupApproved
{
    public static bool IsDisabled(byte[]? value) => value is { Length: >= 1 } bytes && (bytes[0] & 0x01) != 0;
}
