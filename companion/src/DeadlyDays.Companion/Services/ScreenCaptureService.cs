using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeadlyDays.Companion.Models;

namespace DeadlyDays.Companion.Services;

/// <summary>
/// Captures the Deadly Days client. We first ask Windows to render the HWND directly so the
/// companion can keep scanning when another window overlaps the game. Some DirectX/Unreal
/// configurations refuse PrintWindow, so a validated desktop BitBlt remains as fallback.
/// </summary>
public sealed class ScreenCaptureService
{
    private const int Srccopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;
    private const uint PwClientOnly = 0x00000001;
    private const uint PwRenderFullContent = 0x00000002;

    public string LastCaptureMode { get; private set; } = "none";

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
            // This is the important path for a real companion: capture the game HWND rather than
            // whatever happens to be visible at its desktop coordinates when the helper is focused.
            if (PrintWindow(window.Hwnd, memoryDc, PwClientOnly | PwRenderFullContent))
            {
                var direct = ToBitmapSource(bitmap);
                if (IsPlausibleGameFrame(direct))
                {
                    LastCaptureMode = "window";
                    return direct;
                }
            }

            // Hardware accelerated games can reject PrintWindow. Keep the old method as a fallback
            // while the game is actually visible. We never silently call a black/stale frame valid.
            if (!BitBlt(memoryDc, 0, 0, window.Width, window.Height, screenDc, window.X, window.Y, Srccopy | CaptureBlt))
            {
                LastCaptureMode = "failed";
                return null;
            }

            var fallback = ToBitmapSource(bitmap);
            if (!IsPlausibleGameFrame(fallback))
            {
                LastCaptureMode = "invalid";
                return null;
            }

            LastCaptureMode = "screen-fallback";
            return fallback;
        }
        finally
        {
            _ = SelectObject(memoryDc, old);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memoryDc);
            _ = ReleaseDC(0, screenDc);
        }
    }

    private static BitmapSource ToBitmapSource(nint bitmap)
    {
        var source = Imaging.CreateBitmapSourceFromHBitmap(
            bitmap,
            0,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static bool IsPlausibleGameFrame(BitmapSource source)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        // Sample sparsely; this runs several times per second. Reject blank/black/stale surfaces,
        // while allowing the game's intentionally dark navy UI.
        var stepX = Math.Max(8, bgra.PixelWidth / 48);
        var stepY = Math.Max(8, bgra.PixelHeight / 32);
        double sum = 0, sumSq = 0;
        var n = 0;
        for (var y = stepY / 2; y < bgra.PixelHeight; y += stepY)
        for (var x = stepX / 2; x < bgra.PixelWidth; x += stepX)
        {
            var i = y * stride + x * 4;
            var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
            var luma = (77 * r + 150 * g + 29 * b) / 256.0;
            sum += luma;
            sumSq += luma * luma;
            n++;
        }
        if (n == 0) return false;
        var mean = sum / n;
        var variance = Math.Max(0, sumSq / n - mean * mean);
        return mean > 4.0 && variance > 18.0;
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

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
