using ListingSearch.Service.Pipeline.Embedding;
using ListingSearch.Service.Search;
using ListingSearch.Service.Telemetry;

namespace ListingSearch.Service.Pipeline.Stages;

/// <summary>
/// Calls <see cref="ISearchIndex.VectorQueryAsync"/> — the dense-retrieval leaf.
/// Builds its own <see cref="SearchIndexFilter"/> from
/// <see cref="SearchTurnContext.Filter"/>, independently of
/// <see cref="LexicalRetrieverStage"/> (SPEC §2.2). This is deliberately the one
/// place in the pipeline where that independence is spelled out twice in the
/// surrounding prose: it is the seam <c>skip-delisted-check-on-vector-path</c>
/// (SPEC §8.6) targets, and a reader skimming only <see cref="LexicalRetrieverStage"/>
/// would otherwise reasonably assume one filter object is threaded through both.
/// </summary>
public sealed class VectorRetrieverStage(ISearchIndex index, SearchOptions options, IEmbeddingProvider embeddingProvider) : ISearchStage
{
    public string Name => "vector_retriever";

    public bool AppliesTo(SearchTurnContext context) => context.Filter is not null;

    public async ValueTask<StageSignal> ExecuteAsync(SearchTurnContext context, CancellationToken cancellationToken)
    {
        var filter = context.Filter!;
        var embedding = await embeddingProvider.EmbedAsync(context.Request.QueryText, cancellationToken).ConfigureAwait(false);

        if (embedding.Degraded)
        {
            // Partial output with a note, not a fabricated empty result — the same
            // contract every other retrieval-path failure follows (SPEC §7).
            context.NoteDegradation(
                SearchDiagnostics.DegradationStages.VectorRetrieval,
                embedding.DegradationKind ?? SearchDiagnostics.DegradationKinds.MalformedEmbedding);
            context.VectorCandidates = [];
            return StageSignal.Continue;
        }

        var result = await index.VectorQueryAsync(filter, embedding.Vector!, options.CandidatePoolSize, cancellationToken)
            .ConfigureAwait(false);

        foreach (var rejectedId in result.Rejected)
        {
            SearchTurnContext.EmitEvent(
                SearchDiagnostics.Events.FilterRejected,
                (SearchDiagnostics.Attributes.ResultListingId, rejectedId),
                (SearchDiagnostics.Attributes.ResultSource, "vector"));
        }

        if (result.Degraded)
        {
            context.NoteDegradation(SearchDiagnostics.DegradationStages.VectorRetrieval, result.DegradationKind ?? "unknown");
        }

        context.VectorCandidates =
            [.. result.Hits.Select(hit => new RetrievedCandidate(
                hit.ListingId, RetrievalPathKind.Vector, hit.RawScore, hit.PriceChf, hit.ManipulationSignal))];

        return StageSignal.Continue;
    }
}
