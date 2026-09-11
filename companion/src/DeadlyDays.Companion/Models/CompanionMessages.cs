using System.Text.Json.Serialization;

namespace DeadlyDays.Companion.Models;

public sealed record GameWindowSnapshot(
    nint Hwnd,
    int ProcessId,
    string ProcessName,
    string Title,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsMinimized)
{
    [JsonIgnore]
    public bool IsUsable => Hwnd != 0 && Width > 0 && Height > 0 && !IsMinimized;
}

public sealed record RecognizedCandidate(string? ItemId, double Confidence, ulong? Hash = null);

public sealed record VisionSnapshot(
    bool AirdropVisible,
    IReadOnlyList<RecognizedCandidate> Candidates,
    DateTimeOffset CapturedAt);

public sealed record CompanionDetectionMessage(
    string Type,
    bool GameRunning,
    string? Status,
    string? CharacterId,
    IReadOnlyList<string>? ItemIds,
    IReadOnlyList<string>? BagCells,
    IReadOnlyList<string?>? AirdropIds,
    IReadOnlyList<double>? AirdropConfidence,
    string? SavePath,
    long? SaveSize,
    DateTimeOffset Timestamp)
{
    public static CompanionDetectionMessage StatusOnly(bool running, string status, string? savePath = null, long? saveSize = null) =>
        new("companion.detection", running, status, null, null, null, null, null, savePath, saveSize, DateTimeOffset.UtcNow);
}

public sealed record RendererMessage(string Type, int? Slot = null, string? ItemId = null, bool? Enabled = null, string? Text = null, double? Score = null);
