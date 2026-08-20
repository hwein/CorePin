using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace IconGen;

/// Writes a classic .ico: ICONDIR, one 16 byte ICONDIRENTRY per frame, then the frame data.
internal static class IcoWriter
{
    private const int HeaderSize = 6;
    private const int EntrySize = 16;
    private const int InfoHeaderSize = 40;

    /// From this size on a frame is stored PNG compressed instead of as a DIB.
    private const int PngFrameSize = 256;

    public static void Write(string path, IReadOnlyList<BitmapSource> frames)
    {
        RequireValidFrames(frames);
        var encoded = frames.Select(Encode).ToList();

        using FileStream file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0);                // idReserved
        writer.Write((ushort)1);                // idType: icon
        writer.Write((ushort)frames.Count);     // idCount

        int offset = HeaderSize + EntrySize * frames.Count;
        for (int i = 0; i < frames.Count; i++)
        {
            byte size = (byte)(frames[i].PixelWidth >= PngFrameSize ? 0 : frames[i].PixelWidth);
            writer.Write(size);                 // bWidth, 0 means 256
            writer.Write(size);                 // bHeight, 0 means 256
            writer.Write((byte)0);              // bColorCount: no palette
            writer.Write((byte)0);              // bReserved
            writer.Write((ushort)1);            // wPlanes
            writer.Write((ushort)32);           // wBitCount
            writer.Write((uint)encoded[i].Length);
            writer.Write((uint)offset);
            offset += encoded[i].Length;
        }

        foreach (byte[] frame in encoded) writer.Write(frame);
    }

    private static void RequireValidFrames(IReadOnlyList<BitmapSource> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("An icon needs at least one frame.", nameof(frames));
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames[i].PixelWidth != frames[i].PixelHeight)
                throw new ArgumentException($"Frame {i} is {frames[i].PixelWidth}x{frames[i].PixelHeight}, not square.", nameof(frames));
            // Beyond 256 the directory entry has no way left to state the size.
            if (frames[i].PixelWidth > PngFrameSize)
                throw new ArgumentException($"{frames[i].PixelWidth} px exceeds the largest storable frame.", nameof(frames));
            if (i > 0 && frames[i].PixelWidth <= frames[i - 1].PixelWidth)
                throw new ArgumentException("Frames must be ordered by ascending size.", nameof(frames));
        }
    }

    private static byte[] Encode(BitmapSource frame) =>
        frame.PixelWidth >= PngFrameSize ? EncodePng(frame) : EncodeDib(frame);

    private static byte[] EncodePng(BitmapSource frame)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        return buffer.ToArray();
    }

    /// BITMAPINFOHEADER with doubled height, bottom-up 32bpp colour rows, then an all-zero AND mask.
    private static byte[] EncodeDib(BitmapSource frame)
    {
        int size = frame.PixelWidth;
        byte[] pixels = CopyBgra(frame);
        int colourStride = size * 4;
        int maskStride = (size + 31) / 32 * 4;
        int imageSize = (colourStride + maskStride) * size;

        var buffer = new MemoryStream(InfoHeaderSize + imageSize);
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(InfoHeaderSize);       // biSize
            writer.Write(size);                 // biWidth
            writer.Write(size * 2);             // biHeight: colour rows plus mask rows
            writer.Write((ushort)1);            // biPlanes
            writer.Write((ushort)32);           // biBitCount
            writer.Write(0);                    // biCompression: BI_RGB
            writer.Write(imageSize);            // biSizeImage
            writer.Write(0);                    // biXPelsPerMeter
            writer.Write(0);                    // biYPelsPerMeter
            writer.Write(0);                    // biClrUsed
            writer.Write(0);                    // biClrImportant
            for (int y = size - 1; y >= 0; y--) writer.Write(pixels, y * colourStride, colourStride);
            writer.Write(new byte[maskStride * size]);
        }
        return buffer.ToArray();
    }

    private static byte[] CopyBgra(BitmapSource bitmap)
    {
        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
