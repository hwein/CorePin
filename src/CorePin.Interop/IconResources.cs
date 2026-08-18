using System.Buffers.Binary;
using System.Runtime.InteropServices;
using static CorePin.Interop.NativeMethods;

namespace CorePin.Interop;

/// Frame selection from a multi-resolution .ico: own directory scan, no System.Drawing.
public static class IconResources
{
    private const int DirectoryHeaderSize = 6;
    private const int DirectoryEntrySize = 16;
    private const uint DefaultDpi = 96;

    public static int TrayIconSizeFor(nint hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return GetSystemMetricsForDpi(SM_CXSMICON, dpi == 0 ? DefaultDpi : dpi);
    }

    /// The returned HICON belongs to the caller; 0 means the byte stream carries no usable frame.
    public static nint LoadNearestFrame(byte[] icoBytes, int desiredSizePx)
    {
        ArgumentNullException.ThrowIfNull(icoBytes);

        int index = SelectFrameIndex(icoBytes, desiredSizePx);
        if (index < 0) return 0;

        int entry = DirectoryHeaderSize + index * DirectoryEntrySize;
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(icoBytes.AsSpan(entry + 8, 4));
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(icoBytes.AsSpan(entry + 12, 4));
        if (length == 0 || offset > (uint)icoBytes.Length || length > (uint)icoBytes.Length - offset)
            return 0;

        var pin = GCHandle.Alloc(icoBytes, GCHandleType.Pinned);
        try
        {
            return CreateIconFromResourceEx(
                pin.AddrOfPinnedObject() + (nint)offset, length, fIcon: true,
                ICON_RESOURCE_VERSION, desiredSizePx, desiredSizePx, LR_DEFAULTCOLOR);
        }
        finally { pin.Free(); }
    }

    public static void Destroy(nint hIcon)
    {
        if (hIcon != 0) DestroyIcon(hIcon);
    }

    /// The largest frame that does not exceed the wanted size, otherwise the smallest one.
    internal static int SelectFrameIndex(ReadOnlySpan<byte> ico, int desiredSizePx)
    {
        if (ico.Length < DirectoryHeaderSize) return -1;
        if (BinaryPrimitives.ReadUInt16LittleEndian(ico.Slice(2, 2)) != 1) return -1;   // idType 1 = icon

        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.Slice(4, 2));
        if (count == 0 || ico.Length < DirectoryHeaderSize + count * DirectoryEntrySize) return -1;

        int fitting = -1, fittingSize = 0;
        int smallest = -1, smallestSize = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            int size = FrameSize(ico, i);
            if (size <= desiredSizePx && size > fittingSize) (fitting, fittingSize) = (i, size);
            if (size < smallestSize) (smallest, smallestSize) = (i, size);
        }
        return fitting >= 0 ? fitting : smallest;
    }

    /// bWidth is a single byte, and 0 in it means 256 pixels.
    private static int FrameSize(ReadOnlySpan<byte> ico, int index)
    {
        byte width = ico[DirectoryHeaderSize + index * DirectoryEntrySize];
        return width == 0 ? 256 : width;
    }
}
