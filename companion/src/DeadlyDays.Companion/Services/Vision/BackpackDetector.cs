using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeadlyDays.Companion.Services.Vision;

public sealed record BackpackDetection(
    IReadOnlyList<string> Cells,
    int CellSize,
    int OriginX,
    int OriginY,
    double Confidence,
    double AverageCellScore,
    double NeighborLeak)
{
    public string Key => string.Join('|', Cells.OrderBy(x => x, StringComparer.Ordinal));
}

/// <summary>
/// Detects the active backpack-cell polyomino from visible UI only.
/// The game uses a regular grid whose period can be recovered from the faint background grid.
/// Active backpack cells are then classified on that recovered lattice by luminance/brown-panel evidence.
/// </summary>
public static class BackpackDetector
{
    private const double ActiveThreshold = 0.40;

    public static BackpackDetection? Detect(BitmapSource source, RewardPanelDetection? rewardPanel)
    {
        if (source.PixelWidth < 600 || source.PixelHeight < 400) return null;
        var pixels = ReadBgra32(source);
        var width = source.PixelWidth;
        var height = source.PixelHeight;

        var grid = EstimateGrid(pixels, width, height, rewardPanel);
        if (grid is null) return null;
        var (period, decorativeXOffset, decorativeYOffset, gridConfidence) = grid.Value;
        if (period < 24) return null;

        // Empirically the item/backpack cell borders sit just a few pixels after the decorative grid lines.
        // Express the correction as a fraction of the recovered period so it scales with resolution/UI scale.
        var bagXOffset = Mod(decorativeXOffset + (int)Math.Round(period * 0.046), period);
        var bagYOffset = Mod(decorativeYOffset + (int)Math.Round(period * 0.015), period);

        var panelRight = rewardPanel is null ? 0 : rewardPanel.Panel.X + rewardPanel.Panel.Width;
        var searchX0 = Math.Max((int)(width * 0.34), panelRight + (int)(height * 0.08));
        var searchX1 = (int)(width * 0.88);
        var searchY0 = (int)(height * 0.30);
        var searchY1 = (int)(height * 0.72);
        if (searchX1 - searchX0 < period * 4 || searchY1 - searchY0 < period * 3) return null;

        var firstX = searchX0 + Mod(bagXOffset - searchX0, period);
        var firstY = searchY0 + Mod(bagYOffset - searchY0, period);
        var active = new Dictionary<(int X, int Y), double>();

        var gy = 0;
        for (var y = firstY; y + period <= searchY1; y += period, gy++)
        {
            var gx = 0;
            for (var x = firstX; x + period <= searchX1; x += period, gx++)
            {
                var score = CellScore(pixels, width, height, x, y, period);
                if (score > ActiveThreshold) active[(gx, gy)] = score;
            }
        }
        if (active.Count < 4) return null;

        var component = LargestComponent(active.Keys);
        if (component.Count < 4 || component.Count > 48) return null;
        var average = component.Average(c => active[c]);

        var componentSet = component.ToHashSet();
        var leakScores = new List<double>();
        foreach (var cell in component)
        {
            foreach (var n in Neighbors(cell))
            {
                if (componentSet.Contains(n) || n.X < 0 || n.Y < 0) continue;
                var x = firstX + n.X * period;
                var y = firstY + n.Y * period;
                if (x < searchX0 - period || y < searchY0 - period || x + period > searchX1 + period || y + period > searchY1 + period) continue;
                leakScores.Add(CellScore(pixels, width, height, x, y, period));
            }
        }
        var leak = leakScores.Count == 0 ? 0 : leakScores.Max();

        var minX = component.Min(c => c.X);
        var minY = component.Min(c => c.Y);
        var cells = component
            .Select(c => (X: c.X - minX, Y: c.Y - minY))
            .OrderBy(c => c.Y).ThenBy(c => c.X)
            .Select(c => $"{c.X},{c.Y}")
            .ToArray();

        var separation = Math.Clamp(1.0 - Math.Max(0, leak) / ActiveThreshold, 0, 1);
        var cellStrength = Math.Clamp((average - ActiveThreshold) / 0.32, 0, 1);
        var sizeEvidence = Math.Clamp(component.Count / 8.0, 0.55, 1.0);
        var confidence = Math.Clamp(cellStrength * 0.48 + separation * 0.24 + gridConfidence * 0.18 + sizeEvidence * 0.10, 0, 1);
        if (confidence < 0.64) return null;

        return new BackpackDetection(
            cells,
            period,
            firstX + minX * period,
            firstY + minY * period,
            confidence,
            average,
            leak);
    }

    private static (int Period, int XOffset, int YOffset, double Confidence)? EstimateGrid(
        byte[] pixels,
        int width,
        int height,
        RewardPanelDetection? panel)
    {
        var panelRight = panel is null ? 0 : panel.Panel.X + panel.Panel.Width;
        var x0 = Math.Max((int)(width * 0.46), panelRight + (int)(height * 0.08));
        var x1 = (int)(width * 0.95);
        var y0 = (int)(height * 0.13);
        var y1 = (int)(height * 0.34);
        if (x1 - x0 < 300) x0 = (int)(width * 0.35);
        if (x1 - x0 < 150 || y1 - y0 < 80) return null;

        var vertical = new double[Math.Max(1, x1 - x0 - 1)];
        for (var x = x0; x < x1 - 1; x++)
        {
            double total = 0;
            var count = 0;
            for (var y = y0; y < y1; y += 3)
            {
                var a = Luma(pixels, width, x, y);
                var b = Luma(pixels, width, x + 1, y);
                total += Math.Abs(a - b);
                count++;
            }
            vertical[x - x0] = count == 0 ? 0 : total / count;
        }

        var horizontal = new double[Math.Max(1, y1 - y0 - 1)];
        for (var y = y0; y < y1 - 1; y++)
        {
            double total = 0;
            var count = 0;
            for (var x = x0; x < x1; x += 3)
            {
                var a = Luma(pixels, width, x, y);
                var b = Luma(pixels, width, x, y + 1);
                total += Math.Abs(a - b);
                count++;
            }
            horizontal[y - y0] = count == 0 ? 0 : total / count;
        }

        var minPeriod = Math.Max(24, (int)Math.Round(height * 0.050));
        var maxPeriod = Math.Max(minPeriod + 2, (int)Math.Round(height * 0.070));
        var mean = vertical.Average();
        var bestPeriod = 0;
        var bestCorr = double.NegativeInfinity;
        for (var lag = minPeriod; lag <= maxPeriod && lag < vertical.Length; lag++)
        {
            double corr = 0;
            var n = vertical.Length - lag;
            for (var i = 0; i < n; i++) corr += (vertical[i] - mean) * (vertical[i + lag] - mean);
            corr /= Math.Max(1, n);
            if (corr > bestCorr) { bestCorr = corr; bestPeriod = lag; }
        }
        if (bestPeriod <= 0) return null;

        var xLocalOffset = StrongestModulo(vertical, bestPeriod);
        var yLocalOffset = StrongestModulo(horizontal, bestPeriod);
        var xOffset = Mod(x0 + xLocalOffset, bestPeriod);
        var yOffset = Mod(y0 + yLocalOffset, bestPeriod);

        var variance = vertical.Select(v => (v - mean) * (v - mean)).Average();
        var corrRatio = variance <= 0.0001 ? 0 : bestCorr / variance;
        var confidence = Math.Clamp((corrRatio - 0.10) / 0.65, 0, 1);
        return confidence < 0.30 ? null : (bestPeriod, xOffset, yOffset, confidence);
    }

    private static int StrongestModulo(IReadOnlyList<double> signal, int period)
    {
        var bestOffset = 0;
        var best = double.NegativeInfinity;
        for (var offset = 0; offset < period; offset++)
        {
            double sum = 0;
            var count = 0;
            for (var i = offset; i < signal.Count; i += period) { sum += signal[i]; count++; }
            var score = count == 0 ? 0 : sum / count;
            if (score > best) { best = score; bestOffset = offset; }
        }
        return bestOffset;
    }

    private static double CellScore(byte[] pixels, int width, int height, int x0, int y0, int size)
    {
        var x1 = Math.Min(width, x0 + size);
        var y1 = Math.Min(height, y0 + size);
        if (x0 < 0 || y0 < 0 || x1 <= x0 || y1 <= y0) return -1;

        var bright = 0;
        var dark = 0;
        var veryBright = 0;
        var brown = 0;
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = (y * width + x) * 4;
                var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
                var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                if (luma > 45) bright++;
                if (luma < 30) dark++;
                if (luma > 100) veryBright++;
                if (r > 65 && r < 175 && g > 35 && g < 135 && b > 25 && b < 120 && r > g * 1.12 && g > b * 0.90) brown++;
                count++;
            }
        }
        if (count == 0) return -1;
        var fBright = bright / (double)count;
        var fDark = dark / (double)count;
        var fVeryBright = veryBright / (double)count;
        var fBrown = brown / (double)count;
        return fBright * 0.50 + fBrown * 0.25 + fVeryBright * 0.20 - fDark * 0.35;
    }

    private static List<(int X, int Y)> LargestComponent(IEnumerable<(int X, int Y)> cells)
    {
        var remaining = cells.ToHashSet();
        var best = new List<(int X, int Y)>();
        while (remaining.Count > 0)
        {
            var start = remaining.First();
            remaining.Remove(start);
            var component = new List<(int X, int Y)> { start };
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var n in Neighbors(current))
                {
                    if (!remaining.Remove(n)) continue;
                    component.Add(n);
                    queue.Enqueue(n);
                }
            }
            if (component.Count > best.Count) best = component;
        }
        return best;
    }

    private static IEnumerable<(int X, int Y)> Neighbors((int X, int Y) c)
    {
        yield return (c.X + 1, c.Y);
        yield return (c.X - 1, c.Y);
        yield return (c.X, c.Y + 1);
        yield return (c.X, c.Y - 1);
    }

    private static double Luma(byte[] pixels, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static int Mod(int value, int mod)
    {
        var r = value % mod;
        return r < 0 ? r + mod : r;
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
}

public sealed record BackpackConsensusResult(bool Stable, IReadOnlyList<string>? Cells, double Confidence, int Votes);

public sealed class BackpackConsensus
{
    private readonly Queue<BackpackDetection?> _window = new();
    private readonly int _windowSize;
    private readonly int _requiredVotes;

    public BackpackConsensus(int windowSize = 5, int requiredVotes = 3)
    {
        _windowSize = Math.Max(3, windowSize);
        _requiredVotes = Math.Clamp(requiredVotes, 2, _windowSize);
    }

    public void Reset() => _window.Clear();

    public BackpackConsensusResult Push(BackpackDetection? detection)
    {
        _window.Enqueue(detection);
        while (_window.Count > _windowSize) _window.Dequeue();
        var groups = _window
            .Where(x => x is not null)
            .Cast<BackpackDetection>()
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Select(g => new { Detection = g.OrderByDescending(x => x.Confidence).First(), Count = g.Count(), Confidence = g.Average(x => x.Confidence) })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Confidence)
            .ToArray();
        if (groups.Length == 0) return new BackpackConsensusResult(false, null, 0, 0);
        var best = groups[0];
        var runnerUp = groups.Length > 1 ? groups[1].Count : 0;
        var stable = best.Count >= _requiredVotes && best.Count > runnerUp && best.Confidence >= 0.68;
        return new BackpackConsensusResult(stable, best.Detection.Cells, best.Confidence, best.Count);
    }
}
