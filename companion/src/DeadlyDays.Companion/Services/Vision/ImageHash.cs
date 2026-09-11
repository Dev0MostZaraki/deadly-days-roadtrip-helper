using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeadlyDays.Companion.Services.Vision;

public static class ImageHash
{
    public static ulong DifferenceHash(BitmapSource source, Int32Rect crop)
    {
        var pixels = ReadBgra32(source, crop);
        var gray = ResizeToGray(pixels, crop.Width, crop.Height, 9, 8);
        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (gray[y * 9 + x] > gray[y * 9 + x + 1]) hash |= 1UL << bit;
                bit++;
            }
        }
        return hash;
    }

    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    public static double Colorfulness(BitmapSource source, Int32Rect crop)
    {
        var pixels = ReadBgra32(source, crop);
        if (pixels.Length == 0) return 0;
        double total = 0;
        var count = 0;
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            total += max - min;
            count++;
        }
        return count == 0 ? 0 : total / count / 255.0;
    }

    private static byte[] ReadBgra32(BitmapSource source, Int32Rect crop)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = crop.Width * 4;
        var buffer = new byte[stride * crop.Height];
        bgra.CopyPixels(crop, buffer, stride, 0);
        return buffer;
    }

    private static byte[] ResizeToGray(byte[] bgra, int srcW, int srcH, int dstW, int dstH)
    {
        var result = new byte[dstW * dstH];
        for (var y = 0; y < dstH; y++)
        {
            var sy = Math.Min(srcH - 1, (int)((y + 0.5) * srcH / dstH));
            for (var x = 0; x < dstW; x++)
            {
                var sx = Math.Min(srcW - 1, (int)((x + 0.5) * srcW / dstW));
                var i = (sy * srcW + sx) * 4;
                var b = bgra[i]; var g = bgra[i + 1]; var r = bgra[i + 2];
                result[y * dstW + x] = (byte)Math.Clamp((int)(0.114 * b + 0.587 * g + 0.299 * r), 0, 255);
            }
        }
        return result;
    }
}
