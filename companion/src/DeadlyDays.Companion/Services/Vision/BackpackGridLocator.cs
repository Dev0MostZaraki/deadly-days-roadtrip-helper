using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeadlyDays.Companion.Services.Vision;

public sealed record BackpackDetection(
    Int32Rect Panel,
    IReadOnlyList<string> Cells,
    double Confidence,
    int Columns,
    int Rows,
    int CellPitch);

/// <summary>
/// Detects the light-brown active backpack surface and converts it into the same normalized
/// cell coordinates used by the web optimizer. The detector is deliberately conservative:
/// it returns null instead of inventing a shape when geometry or cell evidence is weak.
///
/// The current UI uses a stable ~64 px backpack pitch at 1080p and ~20 px leather padding.
/// Both are scaled from the captured client height, while the backpack itself is found from
/// its color/connected geometry rather than a fixed screen coordinate.
/// </summary>
public static class BackpackGridLocator
{
    public static BackpackDetection? Locate(BitmapSource source)
    {
        if (source.PixelWidth < 700 || source.PixelHeight < 500) return null;

        var pixels = ReadBgra32(source);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var step = Math.Clamp((int)Math.Round(height / 360.0), 2, 5);
        var sx0 = (int)(width * 0.45);
        var sx1 = Math.Min(width - 1, (int)(width * 0.93));
        var sy0 = (int)(height * 0.20);
        var sy1 = Math.Min(height - 1, (int)(height * 0.80));

        var gw = Math.Max(1, (sx1 - sx0 + step - 1) / step);
        var gh = Math.Max(1, (sy1 - sy0 + step - 1) / step);
        var mask = new bool[gw * gh];
        for (var gy = 0; gy < gh; gy++)
        {
            var y = Math.Min(sy1, sy0 + gy * step);
            for (var gx = 0; gx < gw; gx++)
            {
                var x = Math.Min(sx1, sx0 + gx * step);
                var i = (y * width + x) * 4;
                mask[gy * gw + gx] = IsActiveBagLeather(pixels[i], pixels[i + 1], pixels[i + 2]);
            }
        }

        var components = Components(mask, gw, gh);
        Candidate? best = null;
        foreach (var c in components)
        {
            if (c.Count * step * step < height * height * 0.0035) continue;
            var rough = new Int32Rect(
                sx0 + c.MinX * step,
                sy0 + c.MinY * step,
                Math.Max(step, (c.MaxX - c.MinX + 1) * step),
                Math.Max(step, (c.MaxY - c.MinY + 1) * step));

            var refined = RefineBounds(pixels, width, height, rough, step * 2);
            if (refined is null) continue;
            var box = refined.Value;

            if (box.Width < height * 0.12 || box.Width > height * 0.42) continue;
            if (box.Height < height * 0.12 || box.Height > height * 0.36) continue;
            if (box.X + box.Width * 0.5 < width * 0.48) continue;

            var pitch = height * (64.0 / 1080.0);
            var padding = height * (20.0 / 1080.0);
            var columns = FitDimension(box.Width, pitch, padding);
            var rows = FitDimension(box.Height, pitch, padding);
            if (columns is < 1 or > 8 || rows is < 1 or > 8) continue;

            var widthError = Math.Abs(box.Width - (columns * pitch + 2 * padding)) / pitch;
            var heightError = Math.Abs(box.Height - (rows * pitch + 2 * padding)) / pitch;
            if (widthError > 0.28 || heightError > 0.28) continue;

            var active = new bool[rows, columns];
            var evidence = new double[rows, columns];
            for (var row = 0; row < rows; row++)
            {
                var cy = (int)Math.Round(box.Y + padding + (row + 0.5) * pitch);
                for (var col = 0; col < columns; col++)
                {
                    var cx = (int)Math.Round(box.X + padding + (col + 0.5) * pitch);
                    var e = CellEvidence(pixels, width, height, cx, cy, pitch);
                    evidence[row, col] = e;
                    active[row, col] = e >= 0.50;
                }
            }

            var cells = NormalizeCells(active);
            if (cells.Count < 4 || !Connected(active)) continue;
            if (!EveryOuterDimensionUsed(active)) continue;

            var activeEvidence = new List<double>();
            var inactiveEvidence = new List<double>();
            for (var row = 0; row < rows; row++)
            for (var col = 0; col < columns; col++)
                (active[row, col] ? activeEvidence : inactiveEvidence).Add(evidence[row, col]);

            var geometry = Math.Clamp(1.0 - (widthError + heightError) / 0.42, 0, 1);
            var minimumActive = activeEvidence.DefaultIfEmpty(0).Min();
            var maximumInactive = inactiveEvidence.DefaultIfEmpty(0).Max();
            var separation = inactiveEvidence.Count == 0
                ? Math.Clamp((minimumActive - 0.50) / 0.35 + 0.72, 0, 1)
                : Math.Clamp((minimumActive - maximumInactive) / 0.42 + 0.62, 0, 1);
            var countScore = Math.Clamp(cells.Count / 10.0, 0.55, 1.0);
            var confidence = Math.Clamp(geometry * 0.50 + separation * 0.36 + countScore * 0.14, 0, 1);
            if (confidence < 0.68) continue;

            var candidate = new Candidate(
                new BackpackDetection(box, cells, confidence, columns, rows, (int)Math.Round(pitch)),
                c.Count);
            if (best is null || candidate.Detection.Confidence > best.Detection.Confidence + 0.04 ||
                (Math.Abs(candidate.Detection.Confidence - best.Detection.Confidence) < 0.04 && candidate.ComponentSize > best.ComponentSize))
                best = candidate;
        }

        return best?.Detection;
    }

    private static double CellEvidence(byte[] pixels, int width, int height, int cx, int cy, double pitch)
    {
        var radius = Math.Max(4, (int)Math.Round(pitch * 0.12));
        double lumaSum = 0;
        var bright = 0;
        var samples = 0;
        for (var y = Math.Max(0, cy - radius); y <= Math.Min(height - 1, cy + radius); y++)
        {
            for (var x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); x++)
            {
                var i = (y * width + x) * 4;
                var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
                var luma = (77 * r + 150 * g + 29 * b) / 256.0;
                lumaSum += luma;
                if (luma >= 44) bright++;
                samples++;
            }
        }
        if (samples == 0) return 0;
        var mean = lumaSum / samples;
        var brightFraction = bright / (double)samples;

        // Dark world/backpack background is normally below ~25 luma. Empty active cells are
        // around the mid-60s in captured 1080p references; item cards are usually brighter.
        var meanScore = Math.Clamp((mean - 24) / 48.0, 0, 1);
        var brightScore = Math.Clamp((brightFraction - 0.08) / 0.72, 0, 1);
        return meanScore * 0.64 + brightScore * 0.36;
    }

    private static int FitDimension(int pixels, double pitch, double padding)
    {
        var bestN = 0;
        var bestError = double.MaxValue;
        for (var n = 1; n <= 8; n++)
        {
            var expected = n * pitch + 2 * padding;
            var error = Math.Abs(expected - pixels);
            if (error >= bestError) continue;
            bestError = error;
            bestN = n;
        }
        return bestN;
    }

    private static IReadOnlyList<string> NormalizeCells(bool[,] active)
    {
        var rows = active.GetLength(0);
        var cols = active.GetLength(1);
        var points = new List<(int X, int Y)>();
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < cols; x++)
            if (active[y, x]) points.Add((x, y));
        if (points.Count == 0) return Array.Empty<string>();
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        return points
            .Select(p => $"{p.X - minX},{p.Y - minY}")
            .OrderBy(s => int.Parse(s.Split(',')[1]))
            .ThenBy(s => int.Parse(s.Split(',')[0]))
            .ToArray();
    }

    private static bool EveryOuterDimensionUsed(bool[,] active)
    {
        var rows = active.GetLength(0);
        var cols = active.GetLength(1);
        for (var y = 0; y < rows; y++)
            if (!Enumerable.Range(0, cols).Any(x => active[y, x])) return false;
        for (var x = 0; x < cols; x++)
            if (!Enumerable.Range(0, rows).Any(y => active[y, x])) return false;
        return true;
    }

    private static bool Connected(bool[,] active)
    {
        var rows = active.GetLength(0);
        var cols = active.GetLength(1);
        (int X, int Y)? start = null;
        var total = 0;
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < cols; x++)
        {
            if (!active[y, x]) continue;
            total++;
            start ??= (x, y);
        }
        if (start is null) return false;

        var seen = new bool[rows, cols];
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(start.Value);
        seen[start.Value.Y, start.Value.X] = true;
        var visited = 0;
        var directions = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            visited++;
            foreach (var (dx, dy) in directions)
            {
                var nx = p.X + dx; var ny = p.Y + dy;
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || seen[ny, nx] || !active[ny, nx]) continue;
                seen[ny, nx] = true;
                queue.Enqueue((nx, ny));
            }
        }
        return visited == total;
    }

    private static Int32Rect? RefineBounds(byte[] pixels, int width, int height, Int32Rect rough, int margin)
    {
        var x0 = Math.Max(0, rough.X - margin);
        var y0 = Math.Max(0, rough.Y - margin);
        var x1 = Math.Min(width - 1, rough.X + rough.Width + margin);
        var y1 = Math.Min(height - 1, rough.Y + rough.Height + margin);
        var minX = int.MaxValue; var minY = int.MaxValue; var maxX = -1; var maxY = -1;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var i = (y * width + x) * 4;
                if (!IsActiveBagLeather(pixels[i], pixels[i + 1], pixels[i + 2])) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        }
        return maxX < minX || maxY < minY ? null : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static bool IsActiveBagLeather(byte b, byte g, byte r)
    {
        return r >= 62 && r <= 150 &&
               g >= 32 && g <= 105 &&
               b >= 22 && b <= 95 &&
               r >= g + 10 && g >= b - 14;
    }

    private static IReadOnlyList<Component> Components(bool[] mask, int width, int height)
    {
        var visited = new bool[mask.Length];
        var result = new List<Component>();
        var neighbors = new[] { (-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1) };
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = y * width + x;
            if (!mask[index] || visited[index]) continue;
            var q = new Queue<(int X, int Y)>();
            q.Enqueue((x, y));
            visited[index] = true;
            var count = 0; var minX = x; var minY = y; var maxX = x; var maxY = y;
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                count++;
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                foreach (var (dx, dy) in neighbors)
                {
                    var nx = p.X + dx; var ny = p.Y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var ni = ny * width + nx;
                    if (!mask[ni] || visited[ni]) continue;
                    visited[ni] = true;
                    q.Enqueue((nx, ny));
                }
            }
            result.Add(new Component(count, minX, minY, maxX, maxY));
        }
        return result;
    }

    private static byte[] ReadBgra32(BitmapSource source)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var buffer = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(buffer, stride, 0);
        return buffer;
    }

    private readonly record struct Component(int Count, int MinX, int MinY, int MaxX, int MaxY);
    private sealed record Candidate(BackpackDetection Detection, int ComponentSize);
}
