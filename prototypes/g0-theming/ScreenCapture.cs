using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace G0Theming;

/// Captured 32-bit BGR bitmap, top-down, stride = Width * 4.
internal sealed class CapturedImage
{
    public CapturedImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public (byte R, byte G, byte B) At(int x, int y)
    {
        int cx = Math.Clamp(x, 0, Width - 1);
        int cy = Math.Clamp(y, 0, Height - 1);
        int i = (cy * Width + cx) * 4;
        return (Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }

    public void Save(string path)
    {
        BitmapSource source = BitmapSource.Create(
            Width, Height, 96, 96, PixelFormats.Bgr32, null, Pixels, Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }
}

internal static class ScreenCapture
{
    /// BitBlt straight off the screen DC — shows what the compositor actually put on screen,
    /// including any backdrop blend.
    public static CapturedImage FromScreen(int x, int y, int width, int height)
    {
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            return Blit(screenDc, memDc => NativeMethods.BitBlt(
                memDc, 0, 0, width, height, screenDc, x, y,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT), width, height);
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// PrintWindow with PW_RENDERFULLCONTENT — the window's own rendering, without whatever
    /// the compositor blends behind it.
    public static CapturedImage FromWindow(IntPtr hwnd, int width, int height)
    {
        IntPtr windowDc = NativeMethods.GetDC(hwnd);
        try
        {
            return Blit(windowDc, memDc => NativeMethods.PrintWindow(
                hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT), width, height);
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(hwnd, windowDc);
        }
    }

    private static CapturedImage Blit(IntPtr sourceDc, Func<IntPtr, bool> draw, int width, int height)
    {
        IntPtr memDc = NativeMethods.CreateCompatibleDC(sourceDc);
        IntPtr bitmap = NativeMethods.CreateCompatibleBitmap(sourceDc, width, height);
        IntPtr previous = NativeMethods.SelectObject(memDc, bitmap);
        try
        {
            draw(memDc);
            NativeMethods.SelectObject(memDc, previous);
            return ReadPixels(sourceDc, bitmap, width, height);
        }
        finally
        {
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memDc);
        }
    }

    private static CapturedImage ReadPixels(IntPtr dc, IntPtr bitmap, int width, int height)
    {
        var info = new NativeMethods.BITMAPINFO();
        info.bmiHeader.biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
        info.bmiHeader.biWidth = width;
        info.bmiHeader.biHeight = -height;          // negative: top-down rows
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = NativeMethods.BI_RGB;

        byte[] buffer = new byte[width * height * 4];
        NativeMethods.GetDIBits(
            dc, bitmap, 0, (uint)height, ref buffer[0], ref info, NativeMethods.DIB_RGB_COLORS);
        return new CapturedImage(width, height, buffer);
    }
}
