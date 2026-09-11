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
    private readonly OverlayService _overlay = new();
    private readonly DispatcherTimer _timer;
    private GameWindowSnapshot? _game;
    private VisionSnapshot? _lastVision;
    private string? _lastCandidateKey;
    private bool _webReady;
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        _saveWatcher.SaveChanged += (_, _) => Dispatcher.Invoke(PublishStatus);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
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
        PublishStatus();
        if (_game is null || !_game.IsUsable)
        {
            _overlay.Hide();
            return;
        }

        _overlay.UpdateGameStatus(_game, "Deadly Days erkannt", "Live-Erkennung aktiv");
        if (DateTimeOffset.UtcNow - _lastScan >= TimeSpan.FromSeconds(1.2))
            await ScanAsync();
    }

    private Task ScanAsync()
    {
        _lastScan = DateTimeOffset.UtcNow;
        if (_game is null || !_game.IsUsable) return Task.CompletedTask;
        var frame = _capture.Capture(_game);
        if (frame is null) return Task.CompletedTask;
        _lastVision = _vision.Analyze(frame);

        if (_lastVision.AirdropVisible)
        {
            var ids = _lastVision.Candidates.Select(c => c.ItemId).ToArray();
            var key = string.Join('|', ids.Select(x => x ?? "?"));
            StatusText.Text = ids.All(x => x is not null)
                ? $"Airdrop erkannt: {string.Join(" · ", ids!)}"
                : "Airdrop erkannt – Referenzen fehlen noch; Lernmodus möglich.";

            if (key != _lastCandidateKey)
            {
                _lastCandidateKey = key;
                PublishDetection(ids, _lastVision.Candidates.Select(c => c.Confidence).ToArray());
            }
        }
        else
        {
            _lastCandidateKey = null;
        }
        return Task.CompletedTask;
    }

    private void PublishStatus()
    {
        var save = _saveWatcher.GetCurrentSave();
        var status = _game is { IsUsable: true }
            ? $"Deadly Days läuft · {_game.Width}×{_game.Height} · Save {(save is null ? "nicht gefunden" : "gefunden")}"
            : "Warte auf DDSurvivors-Win64-Shipping.exe…";
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
            "Airdrop erkannt",
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
                break;
            case "companion.learnCandidate":
                if (msg.Slot is int slot && !string.IsNullOrWhiteSpace(msg.ItemId))
                {
                    var ok = _vision.LearnCandidate(slot, msg.ItemId!);
                    Post(new { type = "companion.learnResult", ok, slot, itemId = msg.ItemId });
                    if (ok) await ScanAsync();
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

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private void OverlayCheck_Changed(object sender, RoutedEventArgs e)
    {
        _overlay.Enabled = OverlayCheck.IsChecked == true;
        if (!_overlay.Enabled) _overlay.Hide();
    }
}
