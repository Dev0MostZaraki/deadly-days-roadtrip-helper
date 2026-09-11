using System.Text.Json;

namespace DeadlyDays.Companion.Services.Vision;

public sealed record HashTemplate(string ItemId, ulong Hash, DateTimeOffset LearnedAt, string ProfileId);

public sealed class TemplateLibrary
{
    private readonly string _path;
    private readonly Dictionary<string, List<HashTemplate>> _templates = new(StringComparer.OrdinalIgnoreCase);

    public TemplateLibrary()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeadlyDaysCompanion");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "item-templates.json");
        Load();
    }

    public void Learn(string itemId, ulong hash, string profileId)
    {
        if (!_templates.TryGetValue(itemId, out var list))
        {
            list = new List<HashTemplate>();
            _templates[itemId] = list;
        }
        if (list.Any(x => ImageHash.Distance(x.Hash, hash) <= 2)) return;
        list.Add(new HashTemplate(itemId, hash, DateTimeOffset.UtcNow, profileId));
        Save();
    }

    public (string? ItemId, double Confidence, int Distance) Match(ulong hash, string profileId)
    {
        string? bestId = null;
        var bestDistance = int.MaxValue;
        foreach (var pair in _templates)
        {
            foreach (var template in pair.Value.Where(t => t.ProfileId == profileId))
            {
                var d = ImageHash.Distance(hash, template.Hash);
                if (d >= bestDistance) continue;
                bestDistance = d;
                bestId = pair.Key;
            }
        }

        if (bestId is null) return (null, 0, int.MaxValue);
        var confidence = Math.Clamp(1.0 - bestDistance / 24.0, 0, 1);
        return bestDistance <= 12 ? (bestId, confidence, bestDistance) : (null, confidence, bestDistance);
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var all = JsonSerializer.Deserialize<List<HashTemplate>>(File.ReadAllText(_path)) ?? new();
            foreach (var t in all)
            {
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
        var all = _templates.Values.SelectMany(x => x).OrderBy(x => x.ItemId).ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }
}
