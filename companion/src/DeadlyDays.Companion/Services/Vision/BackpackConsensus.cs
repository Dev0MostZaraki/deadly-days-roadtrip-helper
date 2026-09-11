namespace DeadlyDays.Companion.Services.Vision;

public sealed record BackpackConsensusResult(
    bool Stable,
    IReadOnlyList<string> Cells,
    double Confidence,
    int FramesSeen,
    int Votes,
    string Status);

/// <summary>
/// A backpack shape is only auto-applied after the same normalized cell set has been observed
/// repeatedly. This prevents a single animation frame or partially occluded item card from
/// changing the optimizer state.
/// </summary>
public sealed class BackpackConsensus
{
    private readonly Queue<BackpackDetection?> _window = new();
    private readonly int _windowSize;
    private readonly int _requiredVotes;
    private readonly double _requiredConfidence;

    public BackpackConsensus(int windowSize = 5, int requiredVotes = 3, double requiredConfidence = 0.72)
    {
        _windowSize = Math.Max(3, windowSize);
        _requiredVotes = Math.Clamp(requiredVotes, 2, _windowSize);
        _requiredConfidence = Math.Clamp(requiredConfidence, 0.55, 0.98);
    }

    public void Reset() => _window.Clear();

    public BackpackConsensusResult Push(BackpackDetection? detection)
    {
        _window.Enqueue(detection);
        while (_window.Count > _windowSize) _window.Dequeue();

        var votes = _window
            .Where(x => x is not null)
            .Select(x => x!)
            .GroupBy(x => Key(x.Cells), StringComparer.Ordinal)
            .Select(g => new
            {
                Key = g.Key,
                Count = g.Count(),
                Confidence = g.Average(x => x.Confidence),
                Cells = g.First().Cells
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Confidence)
            .ToArray();

        if (votes.Length == 0)
            return new BackpackConsensusResult(false, Array.Empty<string>(), 0, _window.Count, 0, "Rucksack noch nicht erkannt");

        var best = votes[0];
        var runnerUp = votes.Length > 1 ? votes[1].Count : 0;
        var stable = _window.Count >= _requiredVotes &&
                     best.Count >= _requiredVotes &&
                     best.Count > runnerUp &&
                     best.Confidence >= _requiredConfidence;

        var status = stable
            ? $"Rucksack stabil: {best.Cells.Count} Felder · {best.Count}/{_window.Count} Frames · {best.Confidence:P0}"
            : $"Rucksack wird bestätigt: {best.Count}/{Math.Max(_requiredVotes, _window.Count)} Frames";
        return new BackpackConsensusResult(stable, best.Cells, best.Confidence, _window.Count, best.Count, status);
    }

    private static string Key(IReadOnlyList<string> cells) => string.Join('|', cells.OrderBy(x => x, StringComparer.Ordinal));
}
