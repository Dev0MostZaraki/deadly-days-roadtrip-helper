namespace DeadlyDays.Companion.Services.Vision;

public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public System.Windows.Int32Rect ToPixels(int width, int height)
    {
        var x = (int)Math.Round(X * width);
        var y = (int)Math.Round(Y * height);
        var w = Math.Max(1, (int)Math.Round(Width * width));
        var h = Math.Max(1, (int)Math.Round(Height * height));
        x = Math.Clamp(x, 0, Math.Max(0, width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, height - 1));
        w = Math.Clamp(w, 1, Math.Max(1, width - x));
        h = Math.Clamp(h, 1, Math.Max(1, height - y));
        return new System.Windows.Int32Rect(x, y, w, h);
    }
}

public sealed record CalibrationProfile(
    string Id,
    double MinAspect,
    double MaxAspect,
    NormalizedRect AirdropPresenceRegion,
    IReadOnlyList<NormalizedRect> CandidateIconRegions)
{
    public static CalibrationProfile FullHd16x9 { get; } = new(
        "16x9-v1",
        1.72,
        1.81,
        new NormalizedRect(0.205, 0.015, 0.19, 0.09),
        new[]
        {
            new NormalizedRect(0.245, 0.135, 0.11, 0.16),
            new NormalizedRect(0.245, 0.43, 0.11, 0.16),
            new NormalizedRect(0.245, 0.73, 0.11, 0.16)
        });

    public static CalibrationProfile? For(int width, int height)
    {
        if (height <= 0) return null;
        var aspect = (double)width / height;
        var p = FullHd16x9;
        return aspect >= p.MinAspect && aspect <= p.MaxAspect ? p : null;
    }
}
