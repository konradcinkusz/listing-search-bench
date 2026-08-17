namespace ListingSearch.Service.Pipeline;

/// <summary>What the scan found, if anything.</summary>
public sealed record ManipulationFinding(string ListingId, string Signal);

/// <summary>
/// Detects instruction-shaped or ranking-manipulative language sitting in a listing's
/// own free text — SPEC C-7. This exists to make the finding <em>observable</em>
/// (<c>ranking.manipulation_ignored</c>), never to make it <em>actionable</em>: the
/// structural half of C-7 is that nothing downstream reads this scanner's output as
/// an input to a score. A pattern list is an incomplete defence by construction — a
/// determined adversary can phrase around any fixed vocabulary — and that is not what
/// the constraint rests on. What the constraint rests on is that
/// <c>HybridRankerStage</c> never accepts a description string as a scoring input in
/// the first place, only token-overlap counts and a cosine similarity computed the
/// same deterministic way for every listing.
/// </summary>
public static class RankingManipulationScanner
{
    private static readonly string[] Signals =
    [
        "click here", "best offer", "ranked number one", "rank #1", "#1 listing",
        "ignore other listings", "boost my score", "top of the results", "guaranteed best",
        "system:", "ignore previous instructions", "you must rank",
    ];

    public static ManipulationFinding? Scan(string listingId, string freeText)
    {
        if (string.IsNullOrWhiteSpace(freeText))
        {
            return null;
        }

        var lowered = freeText.ToLowerInvariant();

        foreach (var signal in Signals)
        {
            if (lowered.Contains(signal, StringComparison.Ordinal))
            {
                return new ManipulationFinding(listingId, signal);
            }
        }

        return null;
    }
}
