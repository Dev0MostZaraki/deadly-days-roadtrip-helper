using System.IO;
using System.Text;
using System.Text.Json;

namespace DeadlyDays.Companion.Services;

public sealed record Ue4ssTelemetrySnapshot(
    bool FileFound,
    bool Live,
    string TelemetryPath,
    DateTimeOffset? LastTimestamp,
    string? LastEventType,
    string? BridgeVersion,
    int DiscoveryCandidates,
    string? LastDiscoveryReason,
    IReadOnlyList<string> RecentRelevantObjects,
    string? Error)
{
    public static Ue4ssTelemetrySnapshot Missing(string path, string? error = null) =>
        new(false, false, path, null, null, null, 0, null, Array.Empty<string>(), error);
}

/// <summary>
/// Consumes read-only JSONL telemetry emitted by the UE4SS DDRCompanionBridge Lua mod.
/// The companion never writes into the game process through this service.
/// </summary>
public sealed class Ue4ssTelemetryService
{
    private readonly string _bridgeRoot;
    private readonly string _telemetryPath;
    private DateTimeOffset _lastRead = DateTimeOffset.MinValue;
    private Ue4ssTelemetrySnapshot _last;

    public Ue4ssTelemetryService()
    {
        _bridgeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeadlyDaysCompanion",
            "Bridge");
        Directory.CreateDirectory(_bridgeRoot);
        _telemetryPath = Path.Combine(_bridgeRoot, "telemetry.jsonl");
        _last = Ue4ssTelemetrySnapshot.Missing(_telemetryPath);
    }

    public Ue4ssTelemetrySnapshot GetSnapshot(bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _lastRead < TimeSpan.FromMilliseconds(300))
            return _last;

        _lastRead = DateTimeOffset.UtcNow;
        _last = ReadSnapshot();
        return _last;
    }

    private Ue4ssTelemetrySnapshot ReadSnapshot()
    {
        if (!File.Exists(_telemetryPath))
            return Ue4ssTelemetrySnapshot.Missing(_telemetryPath);

        try
        {
            var lines = ReadTailLines(_telemetryPath, maxBytes: 2 * 1024 * 1024, maxLines: 2500);
            DateTimeOffset? lastTimestamp = null;
            string? lastType = null;
            string? version = null;
            var discoveryCandidates = 0;
            string? discoveryReason = null;
            var relevant = new LinkedList<string>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch { continue; }
                using (doc)
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;

                    var type = GetString(root, "type");
                    var timestampText = GetString(root, "timestamp");
                    if (DateTimeOffset.TryParse(timestampText, out var timestamp))
                    {
                        if (lastTimestamp is null || timestamp > lastTimestamp)
                        {
                            lastTimestamp = timestamp;
                            lastType = type;
                        }
                    }

                    version ??= GetString(root, "bridgeVersion");

                    if (string.Equals(type, "discovery", StringComparison.OrdinalIgnoreCase))
                    {
                        if (root.TryGetProperty("matched", out var matched) && matched.TryGetInt32(out var count))
                            discoveryCandidates = count;
                        discoveryReason = GetString(root, "reason");

                        if (root.TryGetProperty("objects", out var objects) && objects.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var obj in objects.EnumerateArray())
                            {
                                var name = GetString(obj, "name");
                                if (string.IsNullOrWhiteSpace(name)) continue;
                                relevant.AddLast(name!);
                                while (relevant.Count > 80) relevant.RemoveFirst();
                            }
                        }
                    }
                    else if (string.Equals(type, "new-widget", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = GetString(root, "name");
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            relevant.AddLast(name!);
                            while (relevant.Count > 80) relevant.RemoveFirst();
                        }
                    }
                }
            }

            var fileWrite = File.GetLastWriteTimeUtc(_telemetryPath);
            var freshnessAnchor = lastTimestamp ?? new DateTimeOffset(fileWrite, TimeSpan.Zero);
            var live = DateTimeOffset.UtcNow - freshnessAnchor < TimeSpan.FromSeconds(4);

            return new Ue4ssTelemetrySnapshot(
                true,
                live,
                _telemetryPath,
                lastTimestamp,
                lastType,
                version,
                discoveryCandidates,
                discoveryReason,
                relevant.ToArray(),
                null);
        }
        catch (Exception ex)
        {
            return Ue4ssTelemetrySnapshot.Missing(_telemetryPath, ex.Message) with { FileFound = true };
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static IReadOnlyList<string> ReadTailLines(string path, int maxBytes, int maxLines)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var take = (int)Math.Min(stream.Length, maxBytes);
        if (take <= 0) return Array.Empty<string>();

        stream.Seek(-take, SeekOrigin.End);
        var buffer = new byte[take];
        var read = 0;
        while (read < take)
        {
            var n = stream.Read(buffer, read, take - read);
            if (n <= 0) break;
            read += n;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        // If we started in the middle of a line, discard that partial first line.
        if (stream.Length > take)
        {
            var firstNewline = text.IndexOf('\n');
            text = firstNewline >= 0 ? text[(firstNewline + 1)..] : string.Empty;
        }

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Length <= maxLines ? lines : lines[^maxLines..];
    }
}
