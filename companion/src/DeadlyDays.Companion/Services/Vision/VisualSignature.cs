using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeadlyDays.Companion.Services.Vision;

public sealed record VisualSignature(
    ulong DifferenceHash,
    ulong AverageHash,
    ulong EdgeHash,
    byte[] NormalizedLumaGrid)
{
    public const int GridSize = 64;
}

/// <summary>
/// A small, local-only visual fingerprint that is deliberately stronger than one dHash.
/// It combines luminance ordering, average structure, edge structure and a contrast-normalized
/// 8x8 intensity grid. No image pixels are persisted by this type.
/// </summary>
public static class VisualFingerprint
{
    public static VisualSignature Compute(BitmapSource source, Int32Rect crop)
    {
        var bgra = ReadBgra32(source, crop);
        var diffGrid = ResizeToGray(bgra, crop.Width, crop.Height, 9, 8);
        var lumaGrid = ResizeToGray(bgra, crop.Width, crop.Height, 8, 8);
        var edgeGrid = ResizeToGray(bgra, crop.Width, crop.Height, 10, 10);

        var dHash = DifferenceHash(diffGrid);
        var aHash = AverageHash(lumaGrid);
        var eHash = EdgeHash(edgeGrid, 10, 10);
        var normalized = NormalizeContrast(lumaGrid);
        return new VisualSignature(dHash, aHash, eHash, normalized);
    }

    /// <summary>0 = identical, 1 = maximally different.</summary>
    public static double Distance(VisualSignature a, VisualSignature b)
    {
        var d = BitOperations.PopCount(a.DifferenceHash ^ b.DifferenceHash) / 64.0;
        var av = BitOperations.PopCount(a.AverageHash ^ b.AverageHash) / 64.0;
        var e = BitOperations.PopCount(a.EdgeHash ^ b.EdgeHash) / 64.0;
        var len = Math.Min(a.NormalizedLumaGrid?.Length ?? 0, b.NormalizedLumaGrid?.Length ?? 0);
        double grid = 1;
        if (len > 0)
        {
            double sum = 0;
            for (var i = 0; i < len; i++) sum += Math.Abs(a.NormalizedLumaGrid[i] - b.NormalizedLumaGrid[i]);
            grid = sum / len / 255.0;
        }

        // Edge structure gets the highest weight because item-card rarity/background colors can change.
        return Math.Clamp(d * 0.24 + av * 0.14 + e * 0.34 + grid * 0.28, 0, 1);
    }

    private static ulong DifferenceHash(byte[] grid9x8)
    {
        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (grid9x8[y * 9 + x] > grid9x8[y * 9 + x + 1]) hash |= 1UL << bit;
                bit++;
            }
        }
        return hash;
    }

    private static ulong AverageHash(byte[] gray8x8)
    {
        var mean = gray8x8.Average(x => (double)x);
        ulong hash = 0;
        for (var i = 0; i < 64; i++) if (gray8x8[i] >= mean) hash |= 1UL << i;
        return hash;
    }

    private static ulong EdgeHash(byte[] gray, int width, int height)
    {
        var edges = new double[64];
        var k = 0;
        for (var gy = 0; gy < 8; gy++)
        {
            var y = Math.Clamp(gy + 1, 1, height - 2);
            for (var gx = 0; gx < 8; gx++)
            {
                var x = Math.Clamp(gx + 1, 1, width - 2);
                var left = gray[y * width + x - 1];
                var right = gray[y * width + x + 1];
                var up = gray[(y - 1) * width + x];
                var down = gray[(y + 1) * width + x];
                edges[k++] = Math.Abs(right - left) + Math.Abs(down - up);
            }
        }
        var sorted = edges.OrderBy(v => v).ToArray();
        var median = sorted[sorted.Length / 2];
        ulong hash = 0;
        for (var i = 0; i < edges.Length; i++) if (edges[i] >= median && edges[i] > 5) hash |= 1UL << i;
        return hash;
    }

    private static byte[] NormalizeContrast(byte[] gray)
    {
        var min = gray.Min();
        var max = gray.Max();
        var range = Math.Max(1, max - min);
        var result = new byte[gray.Length];
        for (var i = 0; i < gray.Length; i++) result[i] = (byte)Math.Clamp((gray[i] - min) * 255 / range, 0, 255);
        return result;
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
            var sy = Math.Clamp((int)((y + 0.5) * srcH / dstH), 0, srcH - 1);
            for (var x = 0; x < dstW; x++)
            {
                var sx = Math.Clamp((int)((x + 0.5) * srcW / dstW), 0, srcW - 1);
                var i = (sy * srcW + sx) * 4;
                var b = bgra[i]; var g = bgra[i + 1]; var r = bgra[i + 2];
                result[y * dstW + x] = (byte)Math.Clamp((int)(0.114 * b + 0.587 * g + 0.299 * r), 0, 255);
            }
        }
        return result;
    }
}
