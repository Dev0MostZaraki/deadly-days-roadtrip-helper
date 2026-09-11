namespace DeadlyDays.Companion.Services;

public sealed class SaveGameWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    public string SaveDirectory { get; }
    public string PreferredSavePath { get; }
    public event EventHandler? SaveChanged;

    public SaveGameWatcher()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SaveDirectory = Path.Combine(local, "DDSurvivors", "Saved", "SaveGames");
        PreferredSavePath = Path.Combine(SaveDirectory, "SaveSlot_DDR_0.sav");

        if (!Directory.Exists(SaveDirectory)) return;
        _watcher = new FileSystemWatcher(SaveDirectory, "*.sav")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnChanged;
    }

    public FileInfo? GetCurrentSave()
    {
        if (File.Exists(PreferredSavePath)) return new FileInfo(PreferredSavePath);
        if (!Directory.Exists(SaveDirectory)) return null;
        return new DirectoryInfo(SaveDirectory)
            .EnumerateFiles("*.sav", SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private void OnChanged(object? sender, FileSystemEventArgs e)
    {
        if (!e.FullPath.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) return;
        SaveChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _watcher?.Dispose();
}
