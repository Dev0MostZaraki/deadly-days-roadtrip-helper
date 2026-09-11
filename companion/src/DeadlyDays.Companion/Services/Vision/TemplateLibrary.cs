using System.IO;
using System.Text.Json;

namespace DeadlyDays.Companion.Services.Vision;

public sealed record SignatureTemplate(string ItemId, VisualSignature Signature, DateTimeOffset LearnedAt, string ProfileId);
public sealed record TemplateMatch(string? ItemId, double Confidence, double Distance, double Margin, bool Ambiguous);

public sealed class TemplateLibrary
{
    private readonly string _path;
    private readonly Dictionary<string, List<SignatureTemplate>> _templates = new(StringComparer.OrdinalIgnoreCase);

    public TemplateLibrary()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeadlyDaysCompanion");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "item-signatures-v2.json");
        Load();
    }

    public int TemplateCount => _templates.Values.Sum(x => x.Count);

    public void Learn(string itemId, VisualSignature signature, string profileId)
    {
        if (!_templates.TryGetValue(itemId, out var list))
        {
            list = new List<SignatureTemplate>();
            _templates[itemId] = list;
        }

        // Avoid collecting dozens of essentially identical samples.
        if (list.Any(x => x.ProfileId == profileId && VisualFingerprint.Distance(x.Signature, signature) <= 0.045)) return;
        list.Add(new SignatureTemplate(itemId, signature, DateTimeOffset.UtcNow, profileId));

        // Keep several real-world samples (different upgrade colors/resolutions), but cap local growth.
        var sameProfile = list.Where(x => x.ProfileId == profileId).OrderByDescending(x => x.LearnedAt).ToList();
        if (sameProfile.Count > 12)
        {
            var keep = sameProfile.Take(12).ToHashSet();
            list.RemoveAll(x => x.ProfileId == profileId && !keep.Contains(x));
        }
        Save();
    }

    public TemplateMatch Match(VisualSignature signature, string profileId)
    {
        var byItem = new List<(string ItemId, double Distance)>();
        foreach (var pair in _templates)
        {
            var distances = pair.Value
                .Where(t => string.Equals(t.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                .Select(t => VisualFingerprint.Distance(t.Signature, signature))
                .OrderBy(x => x)
                .Take(3)
                .ToArray();
            if (distances.Length == 0) continue;

            // The nearest sample dominates, with a small benefit from repeated agreement.
            var aggregate = distances[0];
            if (distances.Length >= 2) aggregate = aggregate * 0.82 + distances[1] * 0.18;
            byItem.Add((pair.Key, aggregate));
        }

        if (byItem.Count == 0) return new TemplateMatch(null, 0, 1, 0, false);
        var ranked = byItem.OrderBy(x => x.Distance).ToArray();
        var best = ranked[0];
        var secondDistance = ranked.Length > 1 ? ranked[1].Distance : 1.0;
        var margin = Math.Max(0, secondDistance - best.Distance);

        // Conservative thresholds: a wrong automatic pick is worse than asking for one correction.
        var tooFar = best.Distance > 0.31;
        var ambiguous = ranked.Length > 1 && margin < 0.055;
        var distanceConfidence = Math.Clamp(1.0 - best.Distance / 0.36, 0, 1);
        var marginConfidence = ranked.Length == 1 ? 1.0 : Math.Clamp(margin / 0.13, 0, 1);
        var confidence = distanceConfidence * (0.72 + 0.28 * marginConfidence);

        if (tooFar || ambiguous || confidence < 0.64)
            return new TemplateMatch(null, confidence, best.Distance, margin, ambiguous);
        return new TemplateMatch(best.ItemId, confidence, best.Distance, margin, false);
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var all = JsonSerializer.Deserialize<List<SignatureTemplate>>(File.ReadAllText(_path)) ?? new();
            foreach (var t in all)
            {
                if (t.Signature.NormalizedLumaGrid is not { Length: > 0 }) continue;
                if (!_templates.TryGetValue(t.ItemId, out var list)) _templates[t.ItemId] = list = new();
                list.Add(t);
            }
        }
        catch
        {
            // A corrupt local template cache should never block the companion from starting.
        }
    }

    private void Save()
    {
        var all = _templates.Values.SelectMany(x => x).OrderBy(x => x.ItemId).ThenBy(x => x.LearnedAt).ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }
}
