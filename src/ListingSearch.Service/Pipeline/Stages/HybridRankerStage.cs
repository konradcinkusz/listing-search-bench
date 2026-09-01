using ListingSearch.Service.Search;
using ListingSearch.Service.Telemetry;

namespace ListingSearch.Service.Pipeline.Stages;

/// <summary>
/// Merges the two candidate sets into one ranked list — SPEC B-4, C-5, C-7.
///
/// <para>
/// The whole of C-7 lives in one fact about this stage's data flow: a candidate's
/// <c>ManipulationSignal</c> is read exactly once, to decide whether to emit
/// <c>ranking.manipulation_ignored</c>, and it is never an input to
/// <see cref="RankedCandidate.CombinedScore"/>. There is no flag, no exception path
/// and no code branch that would let it become one — the only two numbers that feed
/// the score are the lexical and vector match values, both computed the same
/// deterministic way for every listing. <c>rerank-boosts-flagged-text</c>
/// (SPEC §8.6) is the mutant that adds that branch back in.
/// </para>
/// </summary>
public sealed class HybridRankerStage(SearchOptions options) : ISearchStage
{
    public string Name => "hybrid_ranker";

    public bool AppliesTo(SearchTurnContext context) => true;

    public ValueTask<StageSignal> ExecuteAsync(SearchTurnContext context, CancellationToken cancellationToken)
    {
        var maxLexical = context.LexicalCandidates.Count == 0 ? 0 : context.LexicalCandidates.Max(c => c.Score);
        var maxVector = context.VectorCandidates.Count == 0 ? 0 : context.VectorCandidates.Max(c => c.Score);

        var merged = new Dictionary<string, MergedCandidate>(StringComparer.Ordinal);

        foreach (var candidate in context.LexicalCandidates)
        {
            merged[candidate.ListingId] = new MergedCandidate(
                candidate.ListingId, candidate.Score, 0, candidate.PriceChf, candidate.ManipulationSignal);
        }

        foreach (var candidate in context.VectorCandidates)
        {
            merged[candidate.ListingId] = merged.TryGetValue(candidate.ListingId, out var existing)
                ? existing with { Vector = candidate.Score, ManipulationSignal = existing.ManipulationSignal ?? candidate.ManipulationSignal }
                : new MergedCandidate(candidate.ListingId, 0, candidate.Score, candidate.PriceChf, candidate.ManipulationSignal);
        }

        var scored = merged.Values.Select(candidate =>
        {
            var normalizedLexical = maxLexical > 0 ? candidate.Lexical / maxLexical : 0;
            var normalizedVector = maxVector > 0 ? candidate.Vector / maxVector : 0;
            var combined = (options.LexicalWeight * normalizedLexical) + (options.VectorWeight * normalizedVector);

            if (context.Request.SoftMaxPrice is { } softMax && candidate.PriceChf > softMax)
            {
                combined *= options.SoftPricePenalty;
            }

            if (candidate.ManipulationSignal is { } signal)
            {
                SearchTurnContext.EmitEvent(
                    SearchDiagnostics.Events.RankingManipulationIgnored,
                    (SearchDiagnostics.Attributes.ManipulationListingId, candidate.ListingId),
                    (SearchDiagnostics.Attributes.ManipulationSignal, signal));
            }

            var attribution = candidate.Lexical > 0 && candidate.Vector > 0
                ? ResultAttribution.Both
                : candidate.Lexical > 0 ? ResultAttribution.Lexical : ResultAttribution.Vector;

            return new RankedCandidate(
                candidate.ListingId, 0, combined, candidate.Lexical, candidate.Vector, attribution, candidate.PriceChf);
        });

        var ordered = context.Request.Sort switch
        {
            SortOrder.PriceAscending => scored.OrderBy(c => c.Price).ThenBy(c => c.ListingId, StringComparer.Ordinal),
            SortOrder.PriceDescending => scored.OrderByDescending(c => c.Price).ThenBy(c => c.ListingId, StringComparer.Ordinal),
            _ => scored.OrderByDescending(c => c.CombinedScore).ThenBy(c => c.ListingId, StringComparer.Ordinal),
        };

        context.Ranked = [.. ordered.Select((candidate, index) => candidate with { Rank = index + 1 })];

        return ValueTask.FromResult(StageSignal.Continue);
    }

    private sealed record MergedCandidate(string ListingId, double Lexical, double Vector, decimal PriceChf, string? ManipulationSignal);
}
