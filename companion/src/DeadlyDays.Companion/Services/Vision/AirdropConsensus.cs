using DeadlyDays.Companion.Models;

namespace DeadlyDays.Companion.Services.Vision;

public sealed record AirdropConsensusResult(
    bool Stable,
    IReadOnlyList<string?> ItemIds,
    IReadOnlyList<double> Confidence,
    int FramesSeen,
    int MinimumVotes,
    string Status);

/// <summary>
/// Prevents one bad frame from changing the run. A candidate must win repeatedly across a
/// sliding window and reach a minimum average confidence before the companion auto-applies it.
/// </summary>
public sealed class AirdropConsensus
{
    private readonly Queue<VisionSnapshot> _window = new();
    private readonly int _windowSize;
    private readonly int _requiredVotes;
    private readonly double _requiredConfidence;

    public AirdropConsensus(int windowSize = 5, int requiredVotes = 3, double requiredConfidence = 0.72)
    {
        _windowSize = Math.Max(3, windowSize);
        _requiredVotes = Math.Clamp(requiredVotes, 2, _windowSize);
        _requiredConfidence = Math.Clamp(requiredConfidence, 0.5, 0.98);
    }

    public void Reset() => _window.Clear();

    public AirdropConsensusResult Push(VisionSnapshot snapshot)
    {
        if (!snapshot.AirdropVisible || snapshot.Candidates.Count != 3)
        {
            Reset();
            return new AirdropConsensusResult(false, new string?[3], new double[3], 0, 0, "Kein Airdrop stabil erkannt.");
        }

        _window.Enqueue(snapshot);
        while (_window.Count > _windowSize) _window.Dequeue();

        var ids = new string?[3];
        var conf = new double[3];
        var minimumVotes = int.MaxValue;
        var allStable = true;

        for (var slot = 0; slot < 3; slot++)
        {
            var votes = _window
                .Select(f => f.Candidates[slot])
                .Where(c => !string.IsNullOrWhiteSpace(c.ItemId))
                .GroupBy(c => c.ItemId!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Id = g.Key,
                    Count = g.Count(),
                    AverageConfidence = g.Average(x => x.Confidence)
                })
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.AverageConfidence)
                .ToArray();

            if (votes.Length == 0)
            {
                allStable = false;
                minimumVotes = Math.Min(minimumVotes, 0);
                continue;
            }

            var winner = votes[0];
            ids[slot] = winner.Id;
            conf[slot] = winner.AverageConfidence;
            minimumVotes = Math.Min(minimumVotes, winner.Count);

            var runnerUpCount = votes.Length > 1 ? votes[1].Count : 0;
            var hasClearMajority = winner.Count >= _requiredVotes && winner.Count > runnerUpCount;
            var enoughConfidence = winner.AverageConfidence >= _requiredConfidence;
            if (!hasClearMajority || !enoughConfidence) allStable = false;
        }

        if (minimumVotes == int.MaxValue) minimumVotes = 0;
        var stable = allStable && _window.Count >= _requiredVotes;
        var minConfidence = conf.Where(x => x > 0).DefaultIfEmpty(0).Min();
        var status = stable
            ? $"Airdrop stabil: {minimumVotes}/{_window.Count} Frames · {minConfidence:P0} min. Confidence"
            : $"Airdrop wird bestätigt: {minimumVotes}/{Math.Max(_requiredVotes, _window.Count)} Frames";

        return new AirdropConsensusResult(stable, ids, conf, _window.Count, minimumVotes, status);
    }
}
