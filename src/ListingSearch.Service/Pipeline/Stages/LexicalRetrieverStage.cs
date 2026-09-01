using ListingSearch.Service.Search;
using ListingSearch.Service.Telemetry;

namespace ListingSearch.Service.Pipeline.Stages;

/// <summary>
/// Calls <see cref="ISearchIndex.QueryAsync"/> — the lexical leaf of the hybrid
/// pipeline. Builds its own <see cref="SearchIndexFilter"/> from
/// <see cref="SearchTurnContext.Filter"/> rather than passing the shared object
/// through, matching <see cref="VectorRetrieverStage"/>'s independent construction
/// (SPEC §2.2) — the two are peers, not one calling the other.
/// </summary>
public sealed class LexicalRetrieverStage(ISearchIndex index, SearchOptions options) : ISearchStage
{
    public string Name => "lexical_retriever";

    public bool AppliesTo(SearchTurnContext context) => context.Filter is not null;

    public async ValueTask<StageSignal> ExecuteAsync(SearchTurnContext context, CancellationToken cancellationToken)
    {
        var filter = context.Filter!;

        var result = await index.QueryAsync(filter, context.Tokens, options.CandidatePoolSize, cancellationToken)
            .ConfigureAwait(false);

        foreach (var rejectedId in result.Rejected)
        {
            SearchTurnContext.EmitEvent(
                SearchDiagnostics.Events.FilterRejected,
                (SearchDiagnostics.Attributes.ResultListingId, rejectedId),
                (SearchDiagnostics.Attributes.ResultSource, "lexical"));
        }

        if (result.Degraded)
        {
            context.NoteDegradation(SearchDiagnostics.DegradationStages.LexicalRetrieval, result.DegradationKind ?? "unknown");
        }

        context.LexicalCandidates =
            [.. result.Hits.Select(hit => new RetrievedCandidate(
                hit.ListingId, RetrievalPathKind.Lexical, hit.RawScore, hit.PriceChf, hit.ManipulationSignal))];

        return StageSignal.Continue;
    }
}
