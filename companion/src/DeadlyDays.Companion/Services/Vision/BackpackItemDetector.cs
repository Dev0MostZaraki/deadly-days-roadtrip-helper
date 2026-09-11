using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeadlyDays.Companion.Services.Vision;

public sealed record BackpackItemObservation(
    string? ItemId,
    IReadOnlyList<string> Cells,
    Int32Rect Bounds,
    double Confidence,
    double VisualConfidence,
    int WidthCells,
    int HeightCells)
{
    public string GeometryKey => string.Join('|', Cells.OrderBy(x => x, StringComparer.Ordinal));
}

/// <summary>
/// Detects the bright item-card polyominoes placed on top of the darker active backpack leather.
/// The detector uses the already recovered backpack grid as a hard geometric constraint, so a
/// visual match cannot claim cells that do not exist in the current backpack.
/// </summary>
public static class BackpackItemDetector
{
    public const string ProfileId = "backpack-item-v1";

    public static IReadOnlyList<BackpackItemObservation> Detect(
        BitmapSource source,
        BackpackDetection? backpack,
        TemplateLibrary templates)
    {
        if (backpack is null || backpack.Cells.Count == 0 || backpack.CellSize < 12)
            return Array.Empty<BackpackItemObservation>();

        var bagCells = backpack.Cells.Select(ParseCell).ToArray();
        var maxX = bagCells.Max(c => c.X);
        var maxY = bagCells.Max(c => c.Y);
        var cell = backpack.CellSize;
        var margin = Math.Max(3, (int)Math.Round(cell * 0.10));
        var x0 = Math.Max(0, backpack.OriginX - margin);
        var y0 = Math.Max(0, backpack.OriginY - margin);
        var x1 = Math.Min(source.PixelWidth - 1, backpack.OriginX + (maxX + 1) * cell + margin);
        var y1 = Math.Min(source.PixelHeight - 1, backpack.OriginY + (maxY + 1) * cell + margin);
        if (x1 <= x0 || y1 <= y0) return Array.Empty<BackpackItemObservation>();

        var pixels = ReadBgra32(source);
        var width = source.PixelWidth;
        var roiW = x1 - x0 + 1;
        var roiH = y1 - y0 + 1;
        var mask = new bool[roiW * roiH];

        for (var ry = 0; ry < roiH; ry++)
        {
            var y = y0 + ry;
            for (var rx = 0; rx < roiW; rx++)
            {
                var x = x0 + rx;
                var i = (y * width + x) * 4;
                mask[ry * roiW + rx] = IsItemCardPixel(pixels[i], pixels[i + 1], pixels[i + 2]);
            }
        }

        var bagSet = backpack.Cells.ToHashSet(StringComparer.Ordinal);
        var results = new List<BackpackItemObservation>();
        foreach (var component in Components(mask, roiW, roiH))
        {
            if (component.Count < cell * cell * 0.16) continue;
            var bx0 = x0 + component.MinX;
            var by0 = y0 + component.MinY;
            var bx1 = x0 + component.MaxX;
            var by1 = y0 + component.MaxY;
            var bw = bx1 - bx0 + 1;
            var bh = by1 - by0 + 1;
            if (bw < cell * 0.55 || bh < cell * 0.55) continue;
            if (bw > cell * 6.2 || bh > cell * 8.2) continue;

            // Recover the actual polyomino by asking how much of this connected card component
            // occupies every real backpack cell, rather than assuming a rectangular item.
            var occupied = new List<(int X, int Y)>();
            foreach (var c in bagCells)
            {
                var cx0 = backpack.OriginX + c.X * cell;
                var cy0 = backpack.OriginY + c.Y * cell;
                var cx1 = cx0 + cell - 1;
                var cy1 = cy0 + cell - 1;
                if (cx1 < bx0 || cy1 < by0 || cx0 > bx1 || cy0 > by1) continue;

                var hits = 0;
                var samples = 0;
                var inner = Math.Max(2, (int)Math.Round(cell * 0.08));
                for (var y = cy0 + inner; y <= cy1 - inner; y += 2)
                for (var x = cx0 + inner; x <= cx1 - inner; x += 2)
                {
                    if (x < x0 || y < y0 || x > x1 || y > y1) continue;
                    var rx = x - x0; var ry = y - y0;
                    samples++;
                    if (mask[ry * roiW + rx] &&
                        rx >= component.MinX && rx <= component.MaxX &&
                        ry >= component.MinY && ry <= component.MaxY)
                        hits++;
                }
                if (samples > 0 && hits / (double)samples >= 0.28)
                    occupied.Add(c);
            }

            if (occupied.Count == 0 || occupied.Count > 12) continue;
            if (!ConnectedCells(occupied)) continue;
            if (occupied.Any(c => !bagSet.Contains($"{c.X},{c.Y}"))) continue;

            var minCellX = occupied.Min(c => c.X);
            var maxCellX = occupied.Max(c => c.X);
            var minCellY = occupied.Min(c => c.Y);
            var maxCellY = occupied.Max(c => c.Y);
            var widthCells = maxCellX - minCellX + 1;
            var heightCells = maxCellY - minCellY + 1;

            // Item cards are aligned to the recovered grid. Reject floating bright UI fragments.
            var expectedX = backpack.OriginX + minCellX * cell;
            var expectedY = backpack.OriginY + minCellY * cell;
            var alignment = 1.0 - Math.Min(1.0,
                (Math.Abs(bx0 - expectedX) + Math.Abs(by0 - expectedY)) / Math.Max(1.0, cell * 0.55));
            if (alignment < 0.22) continue;

            var rect = ClampRect(new Int32Rect(bx0, by0, bw, bh), source.PixelWidth, source.PixelHeight);
            var signature = VisualFingerprint.Compute(source, rect);
            var match = templates.Match(signature, ProfileId);
            var fill = component.Count / Math.Max(1.0, occupied.Count * cell * cell);
            var geometryConfidence = Math.Clamp(alignment * 0.48 + Math.Clamp(fill / 0.55, 0, 1) * 0.52, 0, 1);
            var confidence = match.ItemId is null
                ? geometryConfidence * 0.55
                : Math.Clamp(match.Confidence * 0.72 + geometryConfidence * 0.28, 0, 1);

            results.Add(new BackpackItemObservation(
                match.ItemId,
                occupied.OrderBy(c => c.Y).ThenBy(c => c.X).Select(c => $"{c.X},{c.Y}").ToArray(),
                rect,
                confidence,
                match.Confidence,
                widthCells,
                heightCells));
        }

        // Suppress nested/duplicate threshold components that resolve to the same cell set.
        return results
            .GroupBy(x => x.GeometryKey, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.Confidence).First())
            .OrderBy(x => ParseCell(x.Cells[0]).Y)
            .ThenBy(x => ParseCell(x.Cells[0]).X)
            .ToArray();
    }

    private static bool IsItemCardPixel(byte b, byte g, byte r)
    {
        var luma = (77 * r + 150 * g + 29 * b) / 256.0;
        // Backpack leather in the supplied 0.22.1 frames sits mostly below this level, while
        // rarity/item-card surfaces (tan, gray, green, blue...) stay above it.
        return luma >= 74 && Math.Max(r, Math.Max(g, b)) >= 78;
    }

    private static bool ConnectedCells(IReadOnlyList<(int X, int Y)> cells)
    {
        if (cells.Count <= 1) return true;
        var set = cells.ToHashSet();
        var seen = new HashSet<(int X, int Y)>();
        var q = new Queue<(int X, int Y)>();
        q.Enqueue(cells[0]); seen.Add(cells[0]);
        var d = new[] { (1,0),(-1,0),(0,1),(0,-1) };
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var (dx,dy) in d)
            {
                var n=(p.X+dx,p.Y+dy);
                if (!set.Contains(n) || !seen.Add(n)) continue;
                q.Enqueue(n);
            }
        }
        return seen.Count == cells.Count;
    }

    private static (int X, int Y) ParseCell(string s)
    {
        var p = s.Split(',');
        return (int.Parse(p[0]), int.Parse(p[1]));
    }

    private static Int32Rect ClampRect(Int32Rect r, int width, int height)
    {
        var x=Math.Clamp(r.X,0,width-1); var y=Math.Clamp(r.Y,0,height-1);
        var w=Math.Clamp(r.Width,1,width-x); var h=Math.Clamp(r.Height,1,height-y);
        return new Int32Rect(x,y,w,h);
    }

    private static IReadOnlyList<Component> Components(bool[] mask, int width, int height)
    {
        var visited = new bool[mask.Length];
        var result = new List<Component>();
        var neighbors = new[] { (-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1) };
        for (var y=0;y<height;y++) for (var x=0;x<width;x++)
        {
            var idx=y*width+x;
            if (!mask[idx] || visited[idx]) continue;
            var q=new Queue<(int X,int Y)>(); q.Enqueue((x,y)); visited[idx]=true;
            var count=0; var minX=x; var minY=y; var maxX=x; var maxY=y;
            while(q.Count>0)
            {
                var p=q.Dequeue(); count++; minX=Math.Min(minX,p.X);minY=Math.Min(minY,p.Y);maxX=Math.Max(maxX,p.X);maxY=Math.Max(maxY,p.Y);
                foreach(var (dx,dy) in neighbors)
                {
                    var nx=p.X+dx;var ny=p.Y+dy;
                    if(nx<0||ny<0||nx>=width||ny>=height)continue;
                    var ni=ny*width+nx;if(!mask[ni]||visited[ni])continue;visited[ni]=true;q.Enqueue((nx,ny));
                }
            }
            result.Add(new Component(count,minX,minY,maxX,maxY));
        }
        return result;
    }

    private static byte[] ReadBgra32(BitmapSource source)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride=bgra.PixelWidth*4; var buffer=new byte[stride*bgra.PixelHeight]; bgra.CopyPixels(buffer,stride,0); return buffer;
    }

    private readonly record struct Component(int Count,int MinX,int MinY,int MaxX,int MaxY);
}
