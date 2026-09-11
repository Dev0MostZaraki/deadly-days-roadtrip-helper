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
        SeedBuiltIns();
    }

    public int TemplateCount => _templates.Values.Sum(x => x.Count);

    public void Learn(string itemId, VisualSignature signature, string profileId)
    {
        if (!_templates.TryGetValue(itemId, out var list))
        {
            list = new List<SignatureTemplate>();
            _templates[itemId] = list;
        }

        if (list.Any(x => x.ProfileId == profileId && VisualFingerprint.Distance(x.Signature, signature) <= 0.045)) return;
        list.Add(new SignatureTemplate(itemId, signature, DateTimeOffset.UtcNow, profileId));

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

            var aggregate = distances[0];
            if (distances.Length >= 2) aggregate = aggregate * 0.82 + distances[1] * 0.18;
            byItem.Add((pair.Key, aggregate));
        }

        if (byItem.Count == 0) return new TemplateMatch(null, 0, 1, 0, false);
        var ranked = byItem.OrderBy(x => x.Distance).ToArray();
        var best = ranked[0];
        var secondDistance = ranked.Length > 1 ? ranked[1].Distance : 1.0;
        var margin = Math.Max(0, secondDistance - best.Distance);

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
        }
    }

    private void SeedBuiltIns()
    {
        const string reward = "reward-panel-v2";
        AddSeed("sawed_shotgun", reward,
            576613270618636288UL, 138943232212992UL, 71705121607581696UL,
            new byte[] {0,0,0,0,0,0,0,0,0,0,0,0,0,0,30,0,0,0,0,0,0,30,0,0,0,125,145,153,157,145,41,0,0,96,77,157,157,22,132,0,0,255,182,181,181,182,182,0,0,0,0,0,0,0,30,6,0,0,6,0,0,0,0,0});
        AddSeed("drill", reward,
            145285172336558080UL, 26423041458176UL, 4735671606328329538UL,
            new byte[] {11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,215,163,11,11,11,11,11,11,68,0,11,11,11,11,11,11,255,255,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11,11});
        AddSeed("baseball_bat", reward,
            145250471144194056UL, 144115731355927040UL, 71925170532450304UL,
            new byte[] {0,0,10,10,0,0,0,0,0,49,0,0,0,0,0,0,0,0,49,0,0,0,0,0,0,203,235,255,255,198,198,0,0,213,235,112,112,112,112,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,49,0,0,0,0,0,0});

        const string backpack = "backpack-item-v1";
        AddSeed("knabe_kola", backpack,
            9305038959502443520UL, 108087780650745984UL, 41886375140604002UL,
            new byte[] {85,88,88,88,88,88,88,133,85,90,76,76,76,76,90,133,133,205,205,205,155,155,75,85,85,255,23,121,11,0,60,133,133,255,11,23,23,0,166,85,133,75,11,11,11,0,60,85,85,90,75,75,75,60,90,133,133,88,88,88,88,88,88,85});
        AddSeed("pistol", backpack,
            9259418995210682500UL, 18446708647875706876UL, 16190705577737000706UL,
            new byte[] {7,7,230,230,230,230,230,219,230,238,238,238,238,238,238,230,247,31,59,0,5,59,247,247,247,255,255,5,5,101,255,247,247,255,255,5,5,31,255,247,247,247,247,255,255,5,247,247,230,238,238,238,238,238,238,230,219,230,230,230,230,230,230,219});
    }

    private void AddSeed(string itemId, string profileId, ulong d, ulong a, ulong e, byte[] grid)
    {
        if (!_templates.TryGetValue(itemId, out var list)) _templates[itemId] = list = new();
        var sig = new VisualSignature(d, a, e, grid);
        if (list.Any(x => x.ProfileId == profileId && VisualFingerprint.Distance(x.Signature, sig) <= 0.01)) return;
        list.Add(new SignatureTemplate(itemId, sig, DateTimeOffset.UnixEpoch, profileId));
    }

    private void Save()
    {
        var all = _templates.Values.SelectMany(x => x).Where(x => x.LearnedAt != DateTimeOffset.UnixEpoch)
            .OrderBy(x => x.ItemId).ThenBy(x => x.LearnedAt).ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }
}
