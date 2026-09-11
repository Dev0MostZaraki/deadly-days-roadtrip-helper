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
    private readonly VisionCoordinator _vision = new(new TemplateLibrary());
    private readonly AirdropConsensus _airdropConsensus = new(windowSize: 5, requiredVotes: 3, requiredConfidence: 0.72);
    private readonly OverlayService _overlay = new();
    private readonly DispatcherTimer _timer;
    private GameWindowSnapshot? _game;
    private VisionSnapshot? _lastVision;
    private string? _lastCandidateKey;
    private string? _visionStatus;
    private bool _webReady;
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
        if (_game is null || !_game.IsUsable)
        {
            _airdropConsensus.Reset();
            _lastCandidateKey = null;
            _visionStatus = null;
            _overlay.Hide();
            PublishStatus();
            return;
        }

        if (DateTimeOffset.UtcNow - _lastScan >= TimeSpan.FromMilliseconds(550))
            await ScanAsync();
        PublishStatus();
    }

    private Task ScanAsync()
    {
        _lastScan = DateTimeOffset.UtcNow;
        if (_game is null || !_game.IsUsable) return Task.CompletedTask;
        var frame = _capture.Capture(_game);
        if (frame is null)
        {
            _visionStatus = "Capture fehlgeschlagen";
            return Task.CompletedTask;
        }

        _lastVision = _vision.Analyze(frame);
        var consensus = _airdropConsensus.Push(_lastVision);
        if (!_lastVision.AirdropVisible)
        {
            _lastCandidateKey = null;
            _visionStatus = $"kein Airdrop · {_vision.LearnedTemplateCount} Referenzen";
            return Task.CompletedTask;
        }

        var rawKnown = _lastVision.Candidates.Count(c => c.ItemId is not null);
        if (!consensus.Stable || consensus.ItemIds.Any(x => x is null))
        {
            _visionStatus = $"Airdrop erkannt · {rawKnown}/3 im Einzelbild · {consensus.Status}";
            return Task.CompletedTask;
        }

        var ids = consensus.ItemIds;
        var key = string.Join('|', ids!);
        _visionStatus = $"{consensus.Status} · {string.Join(" · ", ids!)}";
        if (key == _lastCandidateKey) return Task.CompletedTask;

        _lastCandidateKey = key;
        PublishDetection(ids, consensus.Confidence);
        return Task.CompletedTask;
    }

    private void PublishStatus()
    {
        var save = _saveWatcher.GetCurrentSave();
        var status = _game is { IsUsable: true }
            ? $"Deadly Days läuft · {_game.Width}×{_game.Height} · Save {(save is null ? "nicht gefunden" : "gefunden")}"
            : "Warte auf DDSurvivors-Win64-Shipping.exe…";
        if (!string.IsNullOrWhiteSpace(_visionStatus)) status += $" · {_visionStatus}";
        StatusText.Text = status;
        if (!_webReady) return;
        Post(CompanionDetectionMessage.StatusOnly(
            _game is { IsUsable: true },
            status,
            save?.FullName,
            save?.Length));
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
                PublishStatus();
                break;
            case "companion.scan":
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
            // Folder opening is convenience only; the bundle has already been written.
        }
    }

    private void OverlayCheck_Changed(object sender, RoutedEventArgs e)
    {
        _overlay.Enabled = OverlayCheck.IsChecked == true;
        if (!_overlay.Enabled) _overlay.Hide();
    }
}
