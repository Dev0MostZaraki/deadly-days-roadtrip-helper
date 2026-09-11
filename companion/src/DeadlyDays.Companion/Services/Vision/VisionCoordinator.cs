using System.Windows.Media.Imaging;
using DeadlyDays.Companion.Models;

namespace DeadlyDays.Companion.Services.Vision;

public sealed class VisionCoordinator
{
    private readonly TemplateLibrary _templates;
    private BitmapSource? _lastFrame;
    private CalibrationProfile? _lastProfile;

    public VisionCoordinator(TemplateLibrary templates) => _templates = templates;

    public VisionSnapshot Analyze(BitmapSource frame)
    {
        _lastFrame = frame;
        _lastProfile = CalibrationProfile.For(frame.PixelWidth, frame.PixelHeight);
        if (_lastProfile is null)
            return new VisionSnapshot(false, Array.Empty<RecognizedCandidate>(), DateTimeOffset.UtcNow);

        var profile = _lastProfile;
        var presenceRect = profile.AirdropPresenceRegion.ToPixels(frame.PixelWidth, frame.PixelHeight);
        var airdropVisible = ImageHash.Colorfulness(frame, presenceRect) >= 0.12;
        if (!airdropVisible)
            return new VisionSnapshot(false, Array.Empty<RecognizedCandidate>(), DateTimeOffset.UtcNow);

        var candidates = new List<RecognizedCandidate>(3);
        foreach (var region in profile.CandidateIconRegions)
        {
            var rect = region.ToPixels(frame.PixelWidth, frame.PixelHeight);
            var hash = ImageHash.DifferenceHash(frame, rect);
            var match = _templates.Match(hash, profile.Id);
            candidates.Add(new RecognizedCandidate(match.ItemId, match.Confidence, hash));
        }
        return new VisionSnapshot(true, candidates, DateTimeOffset.UtcNow);
    }

    public bool LearnCandidate(int slot, string itemId)
    {
        if (_lastFrame is null || _lastProfile is null) return false;
        if (slot < 0 || slot >= _lastProfile.CandidateIconRegions.Count) return false;
        var rect = _lastProfile.CandidateIconRegions[slot].ToPixels(_lastFrame.PixelWidth, _lastFrame.PixelHeight);
        var hash = ImageHash.DifferenceHash(_lastFrame, rect);
        _templates.Learn(itemId, hash, _lastProfile.Id);
        return true;
    }
}
