using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeadlyDays.Companion.Services.Vision;

public sealed record RewardPanelDetection(Int32Rect Panel, double Confidence)
{
    public IReadOnlyList<Int32Rect> CandidateRegions => new[]
    {
        Slot(0.115),
        Slot(0.425),
        Slot(0.735)
    };

    private Int32Rect Slot(double y)
    {
        var x = Panel.X + (int)Math.Round(Panel.Width * 0.19);
        var w = (int)Math.Round(Panel.Width * 0.62);
        var top = Panel.Y + (int)Math.Round(Panel.Height * y);
        var h = (int)Math.Round(Panel.Height * 0.20);
        return new Int32Rect(x, top, Math.Max(1, w), Math.Max(1, h));
    }
}

/// <summary>
/// Locates the tall red three-choice reward panel from its own frame geometry.
/// This deliberately does not assume a fixed X position: opening/closing the warehouse
/// moves the panel horizontally, while ultrawide/windowed layouts can change the canvas size.
/// </summary>
public static class RewardPanelLocator
{
    public static RewardPanelDetection? Locate(BitmapSource source)
    {
        if (source.PixelWidth < 600 || source.PixelHeight < 400) return null;
        var pixels = ReadBgra32(source);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        const int yStep = 2;
        const int xStep = 2;

        var columnScores = new int[width];
        var yStart = Math.Max(0, (int)(height * 0.015));
        var yEnd = Math.Min(height, (int)(height * 0.97));
        var ySamples = Math.Max(1, (yEnd - yStart + yStep - 1) / yStep);

        for (var x = 0; x < width; x++)
        {
            var score = 0;
            for (var y = yStart; y < yEnd; y += yStep)
            {
                var i = (y * width + x) * 4;
                if (IsFrameRed(pixels[i], pixels[i + 1], pixels[i + 2])) score++;
            }
            columnScores[x] = score;
        }

        var strongColumns = FindRuns(columnScores, (int)Math.Ceiling(ySamples * 0.62), minRun: 4);
        if (strongColumns.Count < 2) return null;

        (Run left, Run right, double score)? bestPair = null;
        var minPanelWidth = height * 0.24;
        var maxPanelWidth = height * 0.42;
        for (var i = 0; i < strongColumns.Count; i++)
        {
            for (var j = i + 1; j < strongColumns.Count; j++)
            {
                var left = strongColumns[i];
                var right = strongColumns[j];
                var span = right.End - left.Start + 1;
                if (span < minPanelWidth || span > maxPanelWidth) continue;
                var pairScore = left.Peak + right.Peak;
                if (bestPair is null || pairScore > bestPair.Value.score)
                    bestPair = (left, right, pairScore);
            }
        }
        if (bestPair is null) return null;

        var bx0 = bestPair.Value.left.Start;
        var bx1 = bestPair.Value.right.End;
        var panelWidth = bx1 - bx0 + 1;
        var rowScores = new int[height];
        var xSamples = Math.Max(1, (panelWidth + xStep - 1) / xStep);
        for (var y = 0; y < height; y++)
        {
            var score = 0;
            for (var x = bx0; x <= bx1; x += xStep)
            {
                var i = (y * width + x) * 4;
                if (IsFrameRed(pixels[i], pixels[i + 1], pixels[i + 2])) score++;
            }
            rowScores[y] = score;
        }

        var strongRows = FindRuns(rowScores, (int)Math.Ceiling(xSamples * 0.55), minRun: 4);
        if (strongRows.Count < 2) return null;
        var top = strongRows.OrderBy(r => r.Start).First();
        var bottom = strongRows.OrderByDescending(r => r.End).First();
        var panelHeight = bottom.End - top.Start + 1;
        if (panelHeight < height * 0.72) return null;

        var panel = new Int32Rect(bx0, top.Start, panelWidth, panelHeight);
        var verticalStrength = Math.Min(1.0, bestPair.Value.score / Math.Max(1.0, ySamples * 2.0));
        var horizontalStrength = Math.Min(1.0, (top.Peak + bottom.Peak) / Math.Max(1.0, xSamples * 2.0));
        var shapeScore = 1.0 - Math.Min(1.0, Math.Abs(panelWidth / (double)panelHeight - 0.355) / 0.20);
        var confidence = Math.Clamp(verticalStrength * 0.45 + horizontalStrength * 0.35 + shapeScore * 0.20, 0, 1);
        return confidence >= 0.62 ? new RewardPanelDetection(panel, confidence) : null;
    }

    private static bool IsFrameRed(byte b, byte g, byte r)
    {
        if (r < 125 || g > 155 || b > 155) return false;
        return r > g * 1.28 && r > b * 1.22 && Math.Abs(g - b) < 90;
    }

    private static List<Run> FindRuns(IReadOnlyList<int> scores, int threshold, int minRun)
    {
        var runs = new List<Run>();
        var start = -1;
        var peak = 0;
        for (var i = 0; i <= scores.Count; i++)
        {
            var active = i < scores.Count && scores[i] >= threshold;
            if (active)
            {
                if (start < 0) { start = i; peak = scores[i]; }
                else peak = Math.Max(peak, scores[i]);
                continue;
            }
            if (start < 0) continue;
            var end = i - 1;
            if (end - start + 1 >= minRun) runs.Add(new Run(start, end, peak));
            start = -1;
            peak = 0;
        }
        return runs;
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

    private readonly record struct Run(int Start, int End, int Peak);
}
