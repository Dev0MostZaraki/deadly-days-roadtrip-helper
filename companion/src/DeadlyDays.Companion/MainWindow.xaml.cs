using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DeadlyDays.Companion.Models;
using DeadlyDays.Companion.Services;
using DeadlyDays.Companion.Services.Vision;
using Microsoft.Web.WebView2.Core;

namespace DeadlyDays.Companion;

public partial class MainWindow : Window
{
    private readonly GameWindowLocator _gameLocator = new();
    private readonly ScreenCaptureService _capture = new();
    private readonly SaveGameWatcher _saveWatcher = new();
    private readonly GameInstallationService _installationService = new();
    private readonly GameDataIndexService _gameDataIndexService = new();
    private readonly GameLogReader _logReader = new();
    private readonly Ue4ssTelemetryService _ue4ssTelemetryService = new();
    private readonly VisionCoordinator _vision = new(new TemplateLibrary());
    private readonly AirdropConsensus _airdropConsensus = new(windowSize: 5, requiredVotes: 3, requiredConfidence: 0.72);
    private readonly BackpackConsensus _backpackConsensus = new(windowSize: 5, requiredVotes: 3);
    private readonly BackpackInventoryConsensus _inventoryConsensus = new(windowSize: 5, requiredVotes: 3, requiredConfidence: 0.74);
    private readonly OverlayService _overlay = new();
    private readonly DispatcherTimer _timer;
    private GameWindowSnapshot? _game;
    private GameInstallationSnapshot? _installation;
    private GameDataIndexSnapshot? _gameDataIndex;
    private GameLogSnapshot? _logSnapshot;
    private Ue4ssTelemetrySnapshot? _ue4ssSnapshot;
    private VisionSnapshot? _lastVision;
    private string? _lastCandidateKey;
    private string? _lastBagKey;
    private string? _lastInventoryKey;
    private string? _lastSourcePublishKey;
    private string? _visionStatus;
    private bool _webReady;
    private bool _indexingGameData;
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _saveWatcher.SaveChanged += (_, _) => Dispatcher.Invoke(PublishStatus);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _timer.Tick += async (_, _) => await TickAsync();
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => _saveWatcher.Dispose();
    }

    private async Task InitializeAsync()
    {
        await Web.EnsureCoreWebView2Async();
        Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Web.CoreWebView2.Settings.AreDevToolsEnabled = true;
        Web.CoreWebView2.WebMessageReceived += WebMessageReceived;

        var bridgePath = Path.Combine(AppContext.BaseDirectory, "companion-web", "companion-bridge.js");
        if (File.Exists(bridgePath))
        {
            var bridge = await File.ReadAllTextAsync(bridgePath);
            await Web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(bridge);
        }

        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Web.CoreWebView2.SetVirtualHostNameToFolderMapping("ddr.local", webRoot, CoreWebView2HostResourceAccessKind.Allow);
        Web.Source = new Uri("https://ddr.local/index.html?companion=1");
        _timer.Start();
    }

    private async Task TickAsync()
    {
        _game = _gameLocator.Find();
        _installation = _installationService.GetSnapshot(_game);
        _logSnapshot = _logReader.GetSnapshot();
        _ue4ssSnapshot = _ue4ssTelemetryService.GetSnapshot();
        await EnsureGameDataIndexAsync();

        if (_game is null || !_game.IsUsable)
        {
            _airdropConsensus.Reset();
            _backpackConsensus.Reset();
            _inventoryConsensus.Reset();
            _lastCandidateKey = null;
            _lastBagKey = null;
            _lastInventoryKey = null;
            _visionStatus = null;
            _overlay.Hide();
            PublishStatus();
            return;
        }

        if (DateTimeOffset.UtcNow - _lastScan >= TimeSpan.FromMilliseconds(550))
            await ScanAsync();
        PublishStatus();
    }

    private async Task EnsureGameDataIndexAsync(bool force = false)
    {
        if (_indexingGameData || _installation is not { Found: true }) return;
        if (!force && _gameDataIndex is { Ready: true } current &&
            string.Equals(current.BuildId, _installation.BuildId, StringComparison.Ordinal) &&
            string.Equals(current.ContentFingerprint, _installation.ContentFingerprint, StringComparison.Ordinal))
            return;

        _indexingGameData = true;
        try
        {
            var installation = _installation;
            _gameDataIndex = await Task.Run(() => _gameDataIndexService.BuildIndex(installation, force));
        }
        finally
        {
            _indexingGameData = false;
        }
    }

    private Task ScanAsync()
    {
        _lastScan = DateTimeOffset.UtcNow;
        if (_game is null || !_game.IsUsable) return Task.CompletedTask;
        var frame = _capture.Capture(_game);
        if (frame is null)
        {
            _visionStatus = $"Capture fehlgeschlagen ({_capture.LastCaptureMode})";
            return Task.CompletedTask;
        }

        _lastVision = _vision.Analyze(frame);
        var parts = new List<string> { $"Capture {_capture.LastCaptureMode}" };

        var bagConsensus = _backpackConsensus.Push(_vision.LastBackpack);
        if (bagConsensus.Stable && bagConsensus.Cells is { Count: > 0 })
        {
            var bagKey = string.Join('|', bagConsensus.Cells.OrderBy(x => x, StringComparer.Ordinal));
            parts.Add($"Rucksack {bagConsensus.Cells.Count} Felder · {bagConsensus.Confidence:P0}");
            if (!string.Equals(bagKey, _lastBagKey, StringComparison.Ordinal))
            {
                _lastBagKey = bagKey;
                PublishBag(bagConsensus.Cells, bagConsensus.Confidence);
            }
        }
        else if (_vision.LastBackpack is not null)
        {
            parts.Add($"Rucksack wird bestätigt · {_vision.LastBackpack.Cells.Count} Felder · {_vision.LastBackpack.Confidence:P0}");
        }

        var inventoryConsensus = _inventoryConsensus.Push(_vision.LastBackpackItems);
        if (_vision.LastBackpackItems.Count > 0)
            parts.Add(inventoryConsensus.Status);
        if (inventoryConsensus.Stable && bagConsensus.Stable && bagConsensus.Cells is { Count: > 0 })
        {
            var inventoryKey = string.Join(';', inventoryConsensus.Items
                .OrderBy(x => x.GeometryKey, StringComparer.Ordinal)
                .Select(x => $"{x.ItemId}@{x.GeometryKey}"));
            if (!string.Equals(inventoryKey, _lastInventoryKey, StringComparison.Ordinal))
            {
                _lastInventoryKey = inventoryKey;
                PublishInventory(inventoryConsensus.Items, bagConsensus.Cells, inventoryConsensus.Confidence);
            }
        }

        var consensus = _airdropConsensus.Push(_lastVision);
        if (!_lastVision.AirdropVisible)
        {
            _lastCandidateKey = null;
            parts.Add($"kein Airdrop · {_vision.LearnedTemplateCount} Referenzen");
            _visionStatus = string.Join(" · ", parts);
            return Task.CompletedTask;
        }

        var rawKnown = _lastVision.Candidates.Count(c => c.ItemId is not null);
        if (!consensus.Stable || consensus.ItemIds.Any(x => x is null))
        {
            parts.Add($"Airdrop · {rawKnown}/3 im Einzelbild · {consensus.Status}");
            _visionStatus = string.Join(" · ", parts);
            return Task.CompletedTask;
        }

        var ids = consensus.ItemIds;
        var key = string.Join('|', ids!);
        parts.Add($"{consensus.Status} · {string.Join(" · ", ids!)}");
        _visionStatus = string.Join(" · ", parts);
        if (key == _lastCandidateKey) return Task.CompletedTask;

        _lastCandidateKey = key;
        PublishDetection(ids, consensus.Confidence);
        return Task.CompletedTask;
    }

    private void PublishStatus()
    {
        var save = _saveWatcher.GetCurrentSave();
        _installation ??= _installationService.GetSnapshot(_game);
        _logSnapshot ??= _logReader.GetSnapshot();
        _ue4ssSnapshot ??= _ue4ssTelemetryService.GetSnapshot();

        var status = _game is { IsUsable: true }
            ? $"Deadly Days läuft · {_game.Width}×{_game.Height} · Save {(save is null ? "nicht gefunden" : "gefunden")}"
            : "Warte auf DDSurvivors-Win64-Shipping.exe…";

        if (_installation is { Found: true } install)
        {
            var build = !string.IsNullOrWhiteSpace(install.BuildId)
                ? $"Build {install.BuildId}"
                : !string.IsNullOrWhiteSpace(install.ContentFingerprint)
                    ? $"Content {install.ContentFingerprint[..Math.Min(8, install.ContentFingerprint.Length)]}"
                    : "Build ?";
            status += $" · Game-Dateien ✓ · {build} · {install.PakCount} PAK / {install.IoStoreCount} IoStore";
            if (install.BuildChanged) status += " · ⚠ neuer Build erkannt";
        }
        else
        {
            status += " · Game-Dateien nicht gefunden";
        }

        if (_gameDataIndex is { Ready: true } index)
            status += $" · Index ✓ {index.TotalFiles} Dateien / {index.CandidateFiles} Kandidaten";
        else if (_indexingGameData)
            status += " · Index läuft…";

        if (_logSnapshot is { Found: true } log)
            status += $" · Log ✓ ({log.RecentSignals.Count} Signale)";

        if (_ue4ssSnapshot is { Live: true } bridge)
            status += $" · UE4SS Bridge LIVE v{bridge.BridgeVersion ?? "?"} · Discovery {bridge.DiscoveryCandidates}";
        else if (_ue4ssSnapshot is { FileFound: true })
            status += " · UE4SS Bridge stale/offline";
        else
            status += " · UE4SS Bridge nicht verbunden";

        if (!string.IsNullOrWhiteSpace(_visionStatus)) status += $" · {_visionStatus}";
        StatusText.Text = status;
        if (!_webReady) return;

        Post(CompanionDetectionMessage.StatusOnly(
            _game is { IsUsable: true },
            status,
            save?.FullName,
            save?.Length));
        PublishSources(save);
    }

    private void PublishSources(FileInfo? save)
    {
        if (!_webReady) return;
        var install = _installation;
        var index = _gameDataIndex;
        var log = _logSnapshot;
        var bridge = _ue4ssSnapshot;
        var key = string.Join('|', new[]
        {
            install?.Found == true ? install.InstallDirectory : null,
            install?.BuildId,
            install?.ContentFingerprint,
            install?.BuildChanged == true ? "changed" : "same",
            index?.Ready == true ? index.IndexPath : null,
            index?.TotalFiles.ToString(),
            index?.CandidateFiles.ToString(),
            save?.FullName,
            save?.Length.ToString(),
            log?.LatestLogPath,
            log?.Length.ToString(),
            log?.RecentSignals.Count.ToString(),
            bridge?.Live == true ? "bridge-live" : bridge?.FileFound == true ? "bridge-stale" : "bridge-none",
            bridge?.LastTimestamp?.ToString("O"),
            bridge?.DiscoveryCandidates.ToString()
        });
        if (string.Equals(key, _lastSourcePublishKey, StringComparison.Ordinal)) return;
        _lastSourcePublishKey = key;

        Post(new
        {
            type = "companion.sources",
            game = new
            {
                found = install?.Found == true,
                installDirectory = install?.InstallDirectory,
                executablePath = install?.ExecutablePath,
                steamRoot = install?.SteamRoot,
                libraryRoot = install?.LibraryRoot,
                appManifestPath = install?.AppManifestPath,
                buildId = install?.BuildId,
                contentFingerprint = install?.ContentFingerprint,
                pakCount = install?.PakCount ?? 0,
                ioStoreCount = install?.IoStoreCount ?? 0,
                packedBytes = install?.PackedBytes ?? 0,
                buildChanged = install?.BuildChanged == true,
                source = install?.Source
            },
            gameDataIndex = new
            {
                ready = index?.Ready == true,
                path = index?.IndexPath,
                totalFiles = index?.TotalFiles ?? 0,
                containerFiles = index?.ContainerFiles ?? 0,
                looseUnrealAssets = index?.LooseUnrealAssets ?? 0,
                localizationFiles = index?.LocalizationFiles ?? 0,
                plainDataFiles = index?.PlainDataFiles ?? 0,
                candidateFiles = index?.CandidateFiles ?? 0,
                error = index?.Error
            },
            save = new
            {
                found = save is not null,
                path = save?.FullName,
                length = save?.Length,
                lastWrite = save?.LastWriteTimeUtc
            },
            log = new
            {
                found = log?.Found == true,
                path = log?.LatestLogPath,
                length = log?.Length,
                lastWrite = log?.LastWrite,
                recentSignals = log?.RecentSignals.TakeLast(8).ToArray() ?? Array.Empty<string>()
            },
            ue4ss = new
            {
                fileFound = bridge?.FileFound == true,
                live = bridge?.Live == true,
                telemetryPath = bridge?.TelemetryPath,
                lastTimestamp = bridge?.LastTimestamp,
                lastEventType = bridge?.LastEventType,
                bridgeVersion = bridge?.BridgeVersion,
                discoveryCandidates = bridge?.DiscoveryCandidates ?? 0,
                discoveryReason = bridge?.LastDiscoveryReason,
                recentRelevantObjects = bridge?.RecentRelevantObjects.TakeLast(20).ToArray() ?? Array.Empty<string>(),
                error = bridge?.Error
            },
            timestamp = DateTimeOffset.UtcNow
        });
    }

    private void PublishBag(IReadOnlyList<string> cells, double confidence)
    {
        if (!_webReady) return;
        var save = _saveWatcher.GetCurrentSave();
        Post(new CompanionDetectionMessage(
            "companion.detection",
            _game is { IsUsable: true },
            $"Rucksack stabil erkannt · {cells.Count} Felder · {confidence:P0}",
            null,
            null,
            cells,
            null,
            null,
            save?.FullName,
            save?.Length,
            DateTimeOffset.UtcNow));
    }

    private void PublishInventory(IReadOnlyList<BackpackItemObservation> items, IReadOnlyList<string> cells, double confidence)
    {
        if (!_webReady) return;
        var save = _saveWatcher.GetCurrentSave();
        var ids = items.Where(x => x.ItemId is not null).Select(x => x.ItemId!).ToArray();
        Post(new CompanionDetectionMessage(
            "companion.detection",
            _game is { IsUsable: true },
            $"Inventar stabil erkannt · {ids.Length} Items · {confidence:P0}",
            null,
            ids,
            cells,
            null,
            null,
            save?.FullName,
            save?.Length,
            DateTimeOffset.UtcNow));

        Post(new
        {
            type = "companion.inventoryGeometry",
            confidence,
            items = items.Select(x => new
            {
                itemId = x.ItemId,
                cells = x.Cells,
                x.WidthCells,
                x.HeightCells,
                x.Confidence,
                x.VisualConfidence
            }).ToArray(),
            timestamp = DateTimeOffset.UtcNow
        });
    }

    private void PublishDetection(IReadOnlyList<string?> ids, IReadOnlyList<double> confidence)
    {
        if (!_webReady) return;
        var save = _saveWatcher.GetCurrentSave();
        Post(new CompanionDetectionMessage(
            "companion.detection",
            _game is { IsUsable: true },
            "Airdrop stabil erkannt",
            null,
            null,
            null,
            ids,
            confidence,
            save?.FullName,
            save?.Length,
            DateTimeOffset.UtcNow));
    }

    private void Post(object payload)
    {
        if (Web.CoreWebView2 is null) return;
        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
    }

    private async void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        RendererMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<RendererMessage>(e.WebMessageAsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return;
        }
        if (msg is null) return;

        switch (msg.Type)
        {
            case "companion.ready":
                _webReady = true;
                _installation = _installationService.GetSnapshot(_game, force: true);
                _logSnapshot = _logReader.GetSnapshot(force: true);
                _ue4ssSnapshot = _ue4ssTelemetryService.GetSnapshot(force: true);
                await EnsureGameDataIndexAsync(force: true);
                PublishStatus();
                break;
            case "companion.scan":
                _installation = _installationService.GetSnapshot(_game, force: true);
                _logSnapshot = _logReader.GetSnapshot(force: true);
                _ue4ssSnapshot = _ue4ssTelemetryService.GetSnapshot(force: true);
                await EnsureGameDataIndexAsync(force: true);
                await ScanAsync();
                PublishStatus();
                break;
            case "companion.learnCandidate":
                if (msg.Slot is int slot && !string.IsNullOrWhiteSpace(msg.ItemId))
                {
                    var ok = _vision.LearnCandidate(slot, msg.ItemId!);
                    _airdropConsensus.Reset();
                    _lastCandidateKey = null;
                    Post(new { type = "companion.learnResult", ok, slot, itemId = msg.ItemId });
                    if (ok) await ScanAsync();
                    PublishStatus();
                }
                break;
            case "companion.overlay":
                _overlay.Enabled = msg.Enabled ?? true;
                if (!_overlay.Enabled) _overlay.Hide();
                break;
            case "companion.overlayRecommendation":
                if (_game is { IsUsable: true })
                    _overlay.UpdateGameStatus(_game, msg.Text ?? "Empfehlung", msg.Score is double s ? $"Score {s:0}/100" : string.Empty);
                break;
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        _installation = _installationService.GetSnapshot(_game, force: true);
        _logSnapshot = _logReader.GetSnapshot(force: true);
        _ue4ssSnapshot = _ue4ssTelemetryService.GetSnapshot(force: true);
        await EnsureGameDataIndexAsync(force: true);
        await ScanAsync();
        PublishStatus();
    }

    private void DiagnosticButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _vision.ExportDiagnosticBundle();
        if (string.IsNullOrWhiteSpace(path))
        {
            _visionStatus = "Noch kein Frame für Diagnose vorhanden";
            PublishStatus();
            return;
        }

        _visionStatus = $"Diagnose lokal gespeichert: {path}";
        PublishStatus();
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void OverlayCheck_Changed(object sender, RoutedEventArgs e)
    {
        _overlay.Enabled = OverlayCheck.IsChecked == true;
        if (!_overlay.Enabled) _overlay.Hide();
    }
}
