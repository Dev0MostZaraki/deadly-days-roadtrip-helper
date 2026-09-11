using System.Text;

namespace DeadlyDays.Companion.Services;

public sealed record GameLogSnapshot(
    bool Found,
    string LogDirectory,
    string? LatestLogPath,
    long? Length,
    DateTimeOffset? LastWrite,
    IReadOnlyList<string> RecentSignals,
    string? Error);

/// <summary>
/// Read-only tail reader for Unreal logs. It only inspects recent text and exposes potentially
/// useful state signals; it never modifies, rotates or locks game logs.
/// </summary>
public sealed class GameLogReader
{
    private static readonly string[] Keywords =
    {
        "item", "inventory", "pickup", "reward", "airdrop", "backpack", "character",
        "save", "load", "craft", "recipe", "weapon", "powerup", "day", "level"
    };

    public string LogDirectory { get; }
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;
    private GameLogSnapshot _last;

    public GameLogReader()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        LogDirectory = Path.Combine(local, "DDSurvivors", "Saved", "Logs");
        _last = new GameLogSnapshot(false, LogDirectory, null, null, null, Array.Empty<string>(), null);
    }

    public GameLogSnapshot GetSnapshot(bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _lastScan < TimeSpan.FromSeconds(2)) return _last;
        _lastScan = DateTimeOffset.UtcNow;
        _last = Read();
        return _last;
    }

    private GameLogSnapshot Read()
    {
        if (!Directory.Exists(LogDirectory))
            return new GameLogSnapshot(false, LogDirectory, null, null, null, Array.Empty<string>(), null);

        try
        {
            var latest = new DirectoryInfo(LogDirectory)
                .EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is null)
                return new GameLogSnapshot(false, LogDirectory, null, null, null, Array.Empty<string>(), null);

            var tail = ReadTail(latest.FullName, 384 * 1024);
            var signals = tail
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(IsInteresting)
                .Select(Clean)
                .Where(x => x.Length > 0)
                .TakeLast(20)
                .ToArray();

            return new GameLogSnapshot(
                true,
                LogDirectory,
                latest.FullName,
                latest.Length,
                latest.LastWriteTimeUtc,
                signals,
                null);
        }
        catch (Exception ex)
        {
            return new GameLogSnapshot(false, LogDirectory, null, null, null, Array.Empty<string>(), ex.Message);
        }
    }

    private static string ReadTail(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var take = (int)Math.Min(maxBytes, stream.Length);
        if (take <= 0) return string.Empty;
        stream.Seek(-take, SeekOrigin.End);
        var buffer = new byte[take];
        var read = 0;
        while (read < take)
        {
            var n = stream.Read(buffer, read, take - read);
            if (n <= 0) break;
            read += n;
        }
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static bool IsInteresting(string line)
    {
        if (line.Length > 4000) return false;
        return Keywords.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static string Clean(string line)
    {
        var value = line.Trim().Replace('\0', ' ');
        if (value.Length > 500) value = value[..500] + "…";
        return value;
    }
}
