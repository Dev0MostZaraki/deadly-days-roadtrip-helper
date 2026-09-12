using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DeadlyDays.Companion.Services;

public sealed record GameDataFileRecord(
    string RelativePath,
    string Extension,
    long Size,
    long LastWriteUtcTicks,
    string Kind,
    bool Candidate,
    IReadOnlyList<string> MatchedTokens);

public sealed record GameDataIndexSnapshot(
    bool Ready,
    string? BuildId,
    string? ContentFingerprint,
    string? InstallDirectory,
    string? IndexPath,
    int TotalFiles,
    int ContainerFiles,
    int LooseUnrealAssets,
    int LocalizationFiles,
    int PlainDataFiles,
    int CandidateFiles,
    IReadOnlyList<GameDataFileRecord> Candidates,
    DateTimeOffset IndexedAt,
    string? Error)
{
    public static GameDataIndexSnapshot Missing(string? error = null) =>
        new(false, null, null, null, null, 0, 0, 0, 0, 0, 0,
            Array.Empty<GameDataFileRecord>(), DateTimeOffset.UtcNow, error);
}

/// <summary>
/// Builds a derived, read-only index of the locally installed Deadly Days files.
///
/// This deliberately does NOT copy or redistribute game assets. The index contains only paths,
/// sizes, timestamps, lightweight categories and candidate hints. Packed Unreal containers are
/// recorded as opaque containers until a dedicated Unreal parser or the in-process telemetry bridge
/// can expose their object metadata safely.
/// </summary>
public sealed class GameDataIndexService
{
    private static readonly string[] InterestingTokens =
    [
        "item", "weapon", "character", "inventory", "reward", "airdrop", "backpack",
        "sticker", "craft", "recipe", "datatable", "powerup", "throwable", "grenade",
        "firework", "president", "knabe", "perk", "upgrade", "rarity", "loot"
    ];

    private static readonly HashSet<string> PlainExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ini", ".json", ".csv", ".txt", ".xml", ".yaml", ".yml"
    };

    private readonly string _stateRoot;
    private readonly string _indexPath;
    private string? _lastKey;
    private GameDataIndexSnapshot _last = GameDataIndexSnapshot.Missing();

    public GameDataIndexService()
    {
        _stateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeadlyDaysCompanion",
            "GameData");
        Directory.CreateDirectory(_stateRoot);
        _indexPath = Path.Combine(_stateRoot, "game-data-index.json");
    }

    public GameDataIndexSnapshot BuildIndex(GameInstallationSnapshot installation, bool force = false)
    {
        if (!installation.Found || string.IsNullOrWhiteSpace(installation.InstallDirectory))
            return GameDataIndexSnapshot.Missing("Deadly Days installation is not available.");

        var key = !string.IsNullOrWhiteSpace(installation.BuildId)
            ? $"steam:{installation.BuildId}"
            : !string.IsNullOrWhiteSpace(installation.ContentFingerprint)
                ? $"content:{installation.ContentFingerprint}"
                : installation.InstallDirectory;

        if (!force && string.Equals(_lastKey, key, StringComparison.Ordinal) && _last.Ready)
            return _last;

        try
        {
            var root = Path.GetFullPath(installation.InstallDirectory!);
            var all = EnumerateFilesSafe(root).ToArray();
            var records = new List<GameDataFileRecord>(all.Length);

            foreach (var path in all)
            {
                FileInfo info;
                try { info = new FileInfo(path); }
                catch { continue; }

                var ext = info.Extension.ToLowerInvariant();
                var relative = Path.GetRelativePath(root, info.FullName).Replace('\\', '/');
                var kind = Classify(ext);
                var matches = MatchTokens(relative);

                // For small plaintext files we can add token evidence from the text itself without
                // persisting the text. This stays local and avoids indexing copyrighted content.
                if (PlainExtensions.Contains(ext) && info.Length <= 4 * 1024 * 1024)
                {
                    foreach (var token in MatchTokensFromText(info.FullName))
                        if (!matches.Contains(token, StringComparer.OrdinalIgnoreCase)) matches.Add(token);
                }

                var candidate = matches.Count > 0 || kind is "loose-unreal" or "localization";
                records.Add(new GameDataFileRecord(
                    relative,
                    ext,
                    info.Length,
                    info.LastWriteTimeUtc.Ticks,
                    kind,
                    candidate,
                    matches.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()));
            }

            var candidates = records
                .Where(x => x.Candidate)
                .OrderByDescending(x => x.MatchedTokens.Count)
                .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Take(4000)
                .ToArray();

            var snapshot = new GameDataIndexSnapshot(
                true,
                installation.BuildId,
                installation.ContentFingerprint,
                root,
                _indexPath,
                records.Count,
                records.Count(x => x.Kind == "container"),
                records.Count(x => x.Kind == "loose-unreal"),
                records.Count(x => x.Kind == "localization"),
                records.Count(x => x.Kind == "plain-data"),
                candidates.Length,
                candidates,
                DateTimeOffset.UtcNow,
                null);

            Persist(snapshot);
            _lastKey = key;
            _last = snapshot;
            return snapshot;
        }
        catch (Exception ex)
        {
            _last = GameDataIndexSnapshot.Missing(ex.Message);
            return _last;
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> files = Array.Empty<string>();
            IEnumerable<string> dirs = Array.Empty<string>();
            try { files = Directory.EnumerateFiles(dir); } catch { }
            try { dirs = Directory.EnumerateDirectories(dir); } catch { }

            foreach (var file in files) yield return file;
            foreach (var child in dirs) pending.Push(child);
        }
    }

    private static string Classify(string ext) => ext switch
    {
        ".pak" or ".utoc" or ".ucas" => "container",
        ".uasset" or ".uexp" or ".ubulk" => "loose-unreal",
        ".locres" or ".locmeta" => "localization",
        _ when PlainExtensions.Contains(ext) => "plain-data",
        ".dll" or ".exe" => "binary",
        _ => "other"
    };

    private static List<string> MatchTokens(string value)
    {
        var normalized = value.ToLowerInvariant();
        return InterestingTokens.Where(normalized.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> MatchTokensFromText(string path)
    {
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var buffer = new char[Math.Min(262_144, (int)Math.Max(4096, stream.Length))];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            text = new string(buffer, 0, read).ToLowerInvariant();
        }
        catch { yield break; }

        foreach (var token in InterestingTokens)
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase)) yield return token;
    }

    private void Persist(GameDataIndexSnapshot snapshot)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(snapshot, options);
            var temp = _indexPath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, _indexPath, true);
        }
        catch
        {
            // Index persistence is diagnostic/cache only. A write failure must never stop the game.
        }
    }
}
