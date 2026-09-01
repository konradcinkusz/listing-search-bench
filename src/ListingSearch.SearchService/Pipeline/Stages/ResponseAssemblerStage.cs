using System.Text.RegularExpressions;
using ListingSearch.SearchService.Search;
using ListingSearch.SearchService.Telemetry;

namespace ListingSearch.SearchService.Pipeline.Stages;

/// <summary>
/// The last stage, and the response-side half of SPEC C-3: maps
/// <see cref="RankedCandidate"/> — which still carries raw per-path scores — onto
/// <see cref="SearchResultItem"/>, which structurally cannot: the DTO has no field
/// capable of holding a raw score, an internal document id or an embedding vector, so
/// there is no "forgot to strip it" failure mode for those three. What this stage
/// checks defensively, independently of what <c>HybridRankerStage</c> decided, is
/// narrower and different: that a listing id reaching assembly is actually a public
/// id and not something that looks like the backend's own internal identifier
/// (<c>InMemoryFixtureIndex</c>'s <c>esdoc-*</c> shape) — a defence that costs nothing
/// because no planned mutation targets it (SPEC §4, "Where enforcement lives").
/// </summary>
public sealed partial class ResponseAssemblerStage : ISearchStage
{
    public string Name => "response_assembler";

    public bool AppliesTo(SearchTurnContext context) => true;

    public ValueTask<StageSignal> ExecuteAsync(SearchTurnContext context, CancellationToken cancellationToken)
    {
        var results = new List<SearchResultItem>();

        foreach (var candidate in context.Ranked.Take(Math.Max(0, context.Request.Top)))
        {
            if (!PublicListingId().IsMatch(candidate.ListingId))
            {
                SearchTurnContext.EmitEvent(
                    SearchDiagnostics.Events.ConstraintViolated,
                    (SearchDiagnostics.Attributes.ResultListingId, candidate.ListingId));
                continue;
            }

            results.Add(new SearchResultItem(
                candidate.ListingId,
                candidate.Rank,
                candidate.Attribution,
                Math.Round(candidate.CombinedScore, 4)));
        }

        var outcome = context.Degradations.Count > 0 ? SearchOutcome.Degraded : SearchOutcome.Completed;

        context.Response = new SearchResponse(results, outcome, context.Degradations);

        return ValueTask.FromResult(StageSignal.Continue);
    }

    [GeneratedRegex(@"^lst-[0-9]{3,5}$", RegexOptions.None)]
    private static partial Regex PublicListingId();
}
