namespace DeadlyDays.Companion.Services.Vision;

public sealed record BackpackInventoryConsensusResult(
    bool Stable,
    IReadOnlyList<BackpackItemObservation> Items,
    int FramesSeen,
    int Votes,
    double Confidence,
    string Status);

/// <summary>
/// Prevents one bad segmentation frame from replacing the user's entire run state. A full
/// inventory snapshot is auto-applied only when every visible item has an ID and the same
/// item+cell geometry wins repeatedly across the sliding window.
/// </summary>
public sealed class BackpackInventoryConsensus
{
    private readonly Queue<IReadOnlyList<BackpackItemObservation>> _window = new();
    private readonly int _windowSize;
    private readonly int _requiredVotes;
    private readonly double _requiredConfidence;

    public BackpackInventoryConsensus(int windowSize = 5, int requiredVotes = 3, double requiredConfidence = 0.74)
    {
        _windowSize = Math.Max(3, windowSize);
        _requiredVotes = Math.Clamp(requiredVotes, 2, _windowSize);
        _requiredConfidence = Math.Clamp(requiredConfidence, 0.55, 0.98);
    }

    public void Reset() => _window.Clear();

    public BackpackInventoryConsensusResult Push(IReadOnlyList<BackpackItemObservation> items)
    {
        if (items.Count == 0 || items.Any(x => string.IsNullOrWhiteSpace(x.ItemId)))
        {
            _window.Clear();
            var known = items.Count(x => !string.IsNullOrWhiteSpace(x.ItemId));
            return new(false, items, 0, 0, items.Count == 0 ? 0 : items.Average(x => x.Confidence),
                items.Count == 0 ? "Keine Items stabil erkannt." : $"Inventar unvollständig: {known}/{items.Count} IDs sicher.");
        }

        _window.Enqueue(items);
        while (_window.Count > _windowSize) _window.Dequeue();

        static string Key(IReadOnlyList<BackpackItemObservation> xs) => string.Join(";",
            xs.OrderBy(x => x.Cells.FirstOrDefault(), StringComparer.Ordinal)
              .ThenBy(x => x.ItemId, StringComparer.OrdinalIgnoreCase)
              .Select(x => $"{x.ItemId}@{x.GeometryKey}"));

        var groups = _window.GroupBy(Key)
            .Select(g => new
            {
                Key = g.Key,
                Count = g.Count(),
                Sample = g.Last(),
                Confidence = g.SelectMany(x => x).Average(x => x.Confidence)
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Confidence)
            .ToArray();

        var best = groups[0];
        var runnerVotes = groups.Length > 1 ? groups[1].Count : 0;
        var stable = _window.Count >= _requiredVotes &&
                     best.Count >= _requiredVotes &&
                     best.Count > runnerVotes &&
                     best.Confidence >= _requiredConfidence;
        var status = stable
            ? $"Inventar stabil: {best.Count}/{_window.Count} Frames · {best.Confidence:P0}"
            : $"Inventar wird bestätigt: {best.Count}/{Math.Max(_requiredVotes, _window.Count)} Frames · {best.Confidence:P0}";
        return new(stable, best.Sample, _window.Count, best.Count, best.Confidence, status);
    }
}
