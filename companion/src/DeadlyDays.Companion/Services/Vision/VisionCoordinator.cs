using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using DeadlyDays.Companion.Models;

namespace DeadlyDays.Companion.Services.Vision;

public sealed class VisionCoordinator
{
    public const string RewardProfileId = "reward-panel-v2";

    private readonly TemplateLibrary _templates;
    private BitmapSource? _lastFrame;
    private RewardPanelDetection? _lastPanel;
    private VisionSnapshot? _lastSnapshot;

    public VisionCoordinator(TemplateLibrary templates) => _templates = templates;

    public int LearnedTemplateCount => _templates.TemplateCount;
    public RewardPanelDetection? LastPanel => _lastPanel;

    public VisionSnapshot Analyze(BitmapSource frame)
    {
        _lastFrame = frame;
        _lastPanel = RewardPanelLocator.Locate(frame);
        if (_lastPanel is null)
            return _lastSnapshot = new VisionSnapshot(false, Array.Empty<RecognizedCandidate>(), DateTimeOffset.UtcNow);

        var candidates = new List<RecognizedCandidate>(3);
        foreach (var rect in _lastPanel.CandidateRegions)
        {
            var signature = VisualFingerprint.Compute(frame, rect);
            var match = _templates.Match(signature, RewardProfileId);
            var confidence = match.Confidence * (0.88 + 0.12 * _lastPanel.Confidence);
            candidates.Add(new RecognizedCandidate(match.ItemId, confidence, signature.DifferenceHash));
        }

        return _lastSnapshot = new VisionSnapshot(true, candidates, DateTimeOffset.UtcNow);
    }

    public bool LearnCandidate(int slot, string itemId)
    {
        if (_lastFrame is null || _lastPanel is null) return false;
        var regions = _lastPanel.CandidateRegions;
        if (slot < 0 || slot >= regions.Count) return false;
        var signature = VisualFingerprint.Compute(_lastFrame, regions[slot]);
        _templates.Learn(itemId, signature, RewardProfileId);
        return true;
    }

    /// <summary>
    /// Writes a user-requested diagnostic bundle locally. It is never uploaded automatically.
    /// The bundle is useful for calibrating unusual resolutions without changing the game or save.
    /// </summary>
    public string? ExportDiagnosticBundle()
    {
        if (_lastFrame is null) return null;
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeadlyDaysCompanion",
            "diagnostics",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(root);
        SavePng(_lastFrame, Path.Combine(root, "frame.png"));

        if (_lastPanel is not null)
        {
            var i = 1;
            foreach (var rect in _lastPanel.CandidateRegions)
            {
                var crop = new CroppedBitmap(_lastFrame, rect);
                crop.Freeze();
                SavePng(crop, Path.Combine(root, $"candidate-{i++}.png"));
            }
        }

        var metadata = new
        {
            capturedAt = DateTimeOffset.Now,
            width = _lastFrame.PixelWidth,
            height = _lastFrame.PixelHeight,
            profile = RewardProfileId,
            learnedTemplates = _templates.TemplateCount,
            panel = _lastPanel is null ? null : new
            {
                x = _lastPanel.Panel.X,
                y = _lastPanel.Panel.Y,
                width = _lastPanel.Panel.Width,
                height = _lastPanel.Panel.Height,
                confidence = _lastPanel.Confidence
            },
            candidates = _lastSnapshot?.Candidates.Select(c => new { c.ItemId, c.Confidence }).ToArray()
        };
        File.WriteAllText(Path.Combine(root, "metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        return root;
    }

    private static void SavePng(BitmapSource source, string path)
    {
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
    }
}
