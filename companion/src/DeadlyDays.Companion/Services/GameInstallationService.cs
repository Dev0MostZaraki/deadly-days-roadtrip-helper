using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DeadlyDays.Companion.Models;
using Microsoft.Win32;

namespace DeadlyDays.Companion.Services;

public sealed record GameInstallationSnapshot(
    bool Found,
    string? InstallDirectory,
    string? ExecutablePath,
    string? SteamRoot,
    string? LibraryRoot,
    string? AppManifestPath,
    string? BuildId,
    long? LastUpdatedUnix,
    int StateFlags,
    int PakCount,
    int IoStoreCount,
    long PackedBytes,
    string? ContentFingerprint,
    bool BuildChanged,
    string Source,
    string? Error)
{
    public static GameInstallationSnapshot Missing(string? error = null) =>
        new(false, null, null, null, null, null, null, null, 0, 0, 0, 0, null, false, "none", error);
}

/// <summary>
/// Read-only locator for Deadly Days: Roadtrip. It prefers the running process path, then
/// falls back to Steam library discovery via libraryfolders.vdf + appmanifest_3026450.acf.
/// It never writes into Steam or the game directory.
/// </summary>
public sealed class GameInstallationService
{
    public const int SteamAppId = 3026450;
    private readonly string _stateRoot;
    private readonly string _lastBuildPath;
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;
    private GameInstallationSnapshot _last = GameInstallationSnapshot.Missing();

    public GameInstallationService()
    {
        _stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeadlyDaysCompanion");
        Directory.CreateDirectory(_stateRoot);
        _lastBuildPath = Path.Combine(_stateRoot, "last-game-build.txt");
    }

    public GameInstallationSnapshot GetSnapshot(GameWindowSnapshot? game, bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _lastScan < TimeSpan.FromSeconds(8)) return _last;
        _lastScan = DateTimeOffset.UtcNow;
        _last = Discover(game);
        return _last;
    }

    private GameInstallationSnapshot Discover(GameWindowSnapshot? game)
    {
        try
        {
            var processExe = TryGetProcessExecutable(game?.ProcessId);
            if (!string.IsNullOrWhiteSpace(processExe))
            {
                var processRoot = FindGameRootFromExecutable(processExe!);
                if (processRoot is not null)
                {
                    var library = FindSteamLibraryFromGamePath(processRoot);
                    var manifest = library is null ? null : Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
                    return BuildSnapshot(processRoot, processExe, library, manifest, "running-process");
                }
            }

            foreach (var library in DiscoverSteamLibraries())
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
                if (!File.Exists(manifest)) continue;
                var acf = ReadTextShared(manifest);
                var installDirName = AcfValue(acf, "installdir");
                if (string.IsNullOrWhiteSpace(installDirName)) continue;
                var gameRoot = Path.Combine(library, "steamapps", "common", installDirName);
                if (!Directory.Exists(gameRoot)) continue;
                var exe = FindShippingExecutable(gameRoot);
                return BuildSnapshot(gameRoot, exe, library, manifest, "steam-manifest");
            }

            return GameInstallationSnapshot.Missing("Deadly Days installation not found through process or Steam libraries.");
        }
        catch (Exception ex)
        {
            return GameInstallationSnapshot.Missing(ex.Message);
        }
    }

    private GameInstallationSnapshot BuildSnapshot(string gameRoot, string? exePath, string? libraryRoot, string? manifestPath, string source)
    {
        var manifestText = !string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath)
            ? ReadTextShared(manifestPath!)
            : string.Empty;

        var buildId = AcfValue(manifestText, "buildid");
        var lastUpdated = ParseLong(AcfValue(manifestText, "LastUpdated"));
        var stateFlags = (int)(ParseLong(AcfValue(manifestText, "StateFlags")) ?? 0);

        var paksRoot = FindPaksDirectory(gameRoot);
        var staticFiles = paksRoot is null
            ? Array.Empty<FileInfo>()
            : new DirectoryInfo(paksRoot)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(f => f.Extension.Equals(".pak", StringComparison.OrdinalIgnoreCase)
                         || f.Extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase)
                         || f.Extension.Equals(".ucas", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var pakCount = staticFiles.Count(f => f.Extension.Equals(".pak", StringComparison.OrdinalIgnoreCase));
        var ioStoreCount = staticFiles.Count(f => f.Extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase)
                                           || f.Extension.Equals(".ucas", StringComparison.OrdinalIgnoreCase));
        var packedBytes = staticFiles.Sum(f => f.Length);
        var fingerprint = staticFiles.Length == 0 ? null : Fingerprint(staticFiles, gameRoot);
        var buildKey = !string.IsNullOrWhiteSpace(buildId) ? $"steam:{buildId}" : fingerprint is null ? null : $"content:{fingerprint}";
        var buildChanged = DetectAndRememberBuild(buildKey);

        var steamRoot = libraryRoot is null ? null : FindSteamRootForLibrary(libraryRoot);
        return new GameInstallationSnapshot(
            true,
            gameRoot,
            exePath ?? FindShippingExecutable(gameRoot),
            steamRoot,
            libraryRoot,
            File.Exists(manifestPath ?? string.Empty) ? manifestPath : null,
            buildId,
            lastUpdated,
            stateFlags,
            pakCount,
            ioStoreCount,
            packedBytes,
            fingerprint,
            buildChanged,
            source,
            null);
    }

    private bool DetectAndRememberBuild(string? buildKey)
    {
        if (string.IsNullOrWhiteSpace(buildKey)) return false;
        string? previous = null;
        try { if (File.Exists(_lastBuildPath)) previous = File.ReadAllText(_lastBuildPath).Trim(); } catch { }
        var changed = !string.IsNullOrWhiteSpace(previous) && !string.Equals(previous, buildKey, StringComparison.Ordinal);
        try { File.WriteAllText(_lastBuildPath, buildKey); } catch { }
        return changed;
    }

    private static string? TryGetProcessExecutable(int? processId)
    {
        if (processId is null or <= 0) return null;
        try
        {
            using var process = Process.GetProcessById(processId.Value);
            return process.MainModule?.FileName;
        }
        catch { return null; }
    }

    private static string? FindGameRootFromExecutable(string exePath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(exePath)!);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "DDSurvivors", "Content", "Paks"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "Content", "Paks")) && File.Exists(exePath))
                return dir.Parent?.FullName ?? dir.FullName;
        }
        return null;
    }

    private static string? FindSteamLibraryFromGamePath(string gameRoot)
    {
        var normalized = Path.GetFullPath(gameRoot);
        var marker = $"{Path.DirectorySeparatorChar}steamapps{Path.DirectorySeparatorChar}common{Path.DirectorySeparatorChar}";
        var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? normalized[..idx] : null;
    }

    private static IEnumerable<string> DiscoverSteamLibraries()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var full = Path.GetFullPath(path.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(full)) roots.Add(full);
            }
            catch { }
        }

        foreach (var root in RegistrySteamRoots()) Add(root);
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        var all = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.ToArray())
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            string text;
            try { text = ReadTextShared(vdf); } catch { continue; }

            foreach (Match m in Regex.Matches(text, "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                AddLibrary(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, "\\\"\\d+\\\"\\s*\\\"([^\\\"]+)\\\""))
                AddLibrary(m.Groups[1].Value);
        }

        return all;

        void AddLibrary(string raw)
        {
            var value = raw.Replace("\\\\", "\\");
            try
            {
                var full = Path.GetFullPath(value.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(full)) all.Add(full);
            }
            catch { }
        }
    }

    private static IEnumerable<string> RegistrySteamRoots()
    {
        var candidates = new (RegistryKey Root, string SubKey, string Value)[]
        {
            (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath")
        };
        foreach (var candidate in candidates)
        {
            string? value = null;
            try { using var key = candidate.Root.OpenSubKey(candidate.SubKey); value = key?.GetValue(candidate.Value) as string; }
            catch { }
            if (!string.IsNullOrWhiteSpace(value)) yield return value!;
        }
    }

    private static string? FindSteamRootForLibrary(string libraryRoot)
    {
        if (File.Exists(Path.Combine(libraryRoot, "steam.exe"))) return libraryRoot;
        return RegistrySteamRoots().FirstOrDefault(r => File.Exists(Path.Combine(r, "steam.exe")));
    }

    private static string? FindShippingExecutable(string gameRoot)
    {
        var expected = Path.Combine(gameRoot, "DDSurvivors", "Binaries", "Win64", "DDSurvivors-Win64-Shipping.exe");
        if (File.Exists(expected)) return expected;
        try
        {
            return Directory.EnumerateFiles(gameRoot, "*Shipping.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch { return null; }
    }

    private static string? FindPaksDirectory(string gameRoot)
    {
        var expected = Path.Combine(gameRoot, "DDSurvivors", "Content", "Paks");
        if (Directory.Exists(expected)) return expected;
        try
        {
            return Directory.EnumerateDirectories(gameRoot, "Paks", SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Contains($"{Path.DirectorySeparatorChar}Content{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    private static string Fingerprint(IEnumerable<FileInfo> files, string root)
    {
        var sb = new StringBuilder();
        foreach (var f in files)
        {
            var rel = Path.GetRelativePath(root, f.FullName).Replace('\\', '/').ToLowerInvariant();
            sb.Append(rel).Append('|').Append(f.Length).Append('|').Append(f.LastWriteTimeUtc.Ticks).Append('\n');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string ReadTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string? AcfValue(string text, string key)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = Regex.Match(text, $"\\\"{Regex.Escape(key)}\\\"\\s*\\\"([^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static long? ParseLong(string? value) => long.TryParse(value, out var n) ? n : null;
}
