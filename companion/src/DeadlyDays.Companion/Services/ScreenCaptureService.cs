using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DeadlyDays.Companion.Models;

namespace DeadlyDays.Companion.Services;

public sealed class ScreenCaptureService
{
    private const int Srccopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;

    public BitmapSource? Capture(GameWindowSnapshot window)
    {
        if (!window.IsUsable) return null;

        var screenDc = GetDC(0);
        if (screenDc == 0) return null;
        var memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == 0)
        {
            _ = ReleaseDC(0, screenDc);
            return null;
        }

        var bitmap = CreateCompatibleBitmap(screenDc, window.Width, window.Height);
        if (bitmap == 0)
        {
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(0, screenDc);
            return null;
        }

        var old = SelectObject(memoryDc, bitmap);
        try
        {
            if (!BitBlt(memoryDc, 0, 0, window.Width, window.Height, screenDc, window.X, window.Y, Srccopy | CaptureBlt))
                return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                0,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            _ = SelectObject(memoryDc, old);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(0, screenDc);
        }
    }

    public static void SavePng(BitmapSource source, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, int rop);
}
