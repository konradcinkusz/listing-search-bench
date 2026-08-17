using Homefinder.SearchService.Ingestion;
using Homefinder.SearchService.Pipeline;
using Homefinder.SearchService.Pipeline.Embedding;
using Homefinder.SearchService.Search;
using Homefinder.SearchService.Telemetry;

namespace Homefinder.Evals.Mutations;

/// <summary>
/// The four deliberately broken variants SPEC §8.6 requires. Each keeps the
/// original stage's <c>Name</c> — indistinguishable in the trace by identity, only by
/// behaviour — and each changes exactly one thing, described in its own doc comment
/// as the single line that differs from the real implementation.
/// </summary>
public sealed class DisablesHardPriceFilterStage : ISearchStage
{
    public string Name => "filter_resolver";

    public bool AppliesTo(SearchTurnContext context) => true;

    public ValueTask<StageSignal> ExecuteAsync(SearchTurnContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;

        // The bug: MinPrice/MaxPrice/MinRooms/MaxRooms from the request are silently
        // dropped. City and the status restriction are copied faithfully — a narrow,
        // realistic mutation, not "the filter resolver does nothing at all".
        context.Filter = new SearchIndexFilter(
            MinPrice: null,
            MaxPrice: null,
            City: request.City,
            MinRooms: null,
            MaxRooms: null,
            AllowedStatuses: [ListingStatus.Active]);

        return ValueTask.FromResult(StageSignal.Continue);
    }
}

public sealed class SkipsDelistedCheckOnVectorPathStage(ISearchIndex index, SearchOptions options) : ISearchStage
{
    public string Name => "vector_retriever";

    public bool AppliesTo(SearchTurnContext context) => context.Filter is not null;

    public async ValueTask<StageSignal> ExecuteAsync(SearchTurnContext context, CancellationToken cancellationToken)
    {
        var original = context.Filter!;

        // The bug: this path rebuilds its own filter — correctly, per SPEC §2.2 —
        // except the status restriction. Every other constraint (price, city, rooms)
        // is copied faithfully; only AllowedStatuses widens to every status. The
        // lexical path, built by the unmodified LexicalRetrieverStage, is unaffected.
        var filter = new SearchIndexFilter(
            original.MinPrice,
            original.MaxPrice,
            original.City,
            original.MinRooms,
            original.MaxRooms,
            AllowedStatuses: [ListingStatus.Active, ListingStatus.Draft, ListingStatus.Delisted, ListingStatus.Expired]);

        var queryEmbedding = DeterministicTextEmbedding.Compute(context.Tokens);
        var result = await index.VectorQueryAsync(filter, queryEmbedding, options.CandidatePoolSize, cancellationToken)
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

public sealed class BoostsFlaggedTextStage(SearchOptions options) : ISearchStage
{
    public string Name => "hybrid_ranker";

    public bool AppliesTo(SearchTurnContext context) => true;

    public ValueTask<StageSignal> ExecuteAsync(SearchTurnContext context, CancellationToken cancellationToken)
    {
        var maxLexical = context.LexicalCandidates.Count == 0 ? 0 : context.LexicalCandidates.Max(c => c.Score);
        var maxVector = context.VectorCandidates.Count == 0 ? 0 : context.VectorCandidates.Max(c => c.Score);

        var merged = new Dictionary<string, Merged>(StringComparer.Ordinal);

        foreach (var candidate in context.LexicalCandidates)
        {
            merged[candidate.ListingId] = new Merged(candidate.ListingId, candidate.Score, 0, candidate.PriceChf, candidate.ManipulationSignal);
        }

        foreach (var candidate in context.VectorCandidates)
        {
            merged[candidate.ListingId] = merged.TryGetValue(candidate.ListingId, out var existing)
                ? existing with { Vector = candidate.Score, ManipulationSignal = existing.ManipulationSignal ?? candidate.ManipulationSignal }
                : new Merged(candidate.ListingId, 0, candidate.Score, candidate.PriceChf, candidate.ManipulationSignal);
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

            var attribution = candidate.Lexical > 0 && candidate.Vector > 0
                ? ResultAttribution.Both
                : candidate.Lexical > 0 ? ResultAttribution.Lexical : ResultAttribution.Vector;

            if (candidate.ManipulationSignal is { } signal)
            {
                SearchTurnContext.EmitEvent(
                    SearchDiagnostics.Events.RankingManipulationIgnored,
                    (SearchDiagnostics.Attributes.ManipulationListingId, candidate.ListingId),
                    (SearchDiagnostics.Attributes.ManipulationSignal, signal));

                // The bug: the event is still (truthfully!) reported as "ignored",
                // and the score is boosted to the top anyway — "helpfully" acting on
                // exactly the signal the report claims was inert.
                combined = 10.0;
            }

            return new RankedCandidate(candidate.ListingId, 0, combined, candidate.Lexical, candidate.Vector, attribution, candidate.PriceChf);
        });

        context.Ranked = [.. scored.OrderByDescending(c => c.CombinedScore).ThenBy(c => c.ListingId, StringComparer.Ordinal)
            .Select((candidate, index) => candidate with { Rank = index + 1 })];

        return ValueTask.FromResult(StageSignal.Continue);
    }

    private sealed record Merged(string ListingId, double Lexical, double Vector, decimal PriceChf, string? ManipulationSignal);
}

/// <summary>
/// The bug: never consults <see cref="IEventIdempotencyStore"/> at all — every event,
/// replay included, is applied as if it were new. "Let's just be helpful and apply it
/// again" is the write-retry failure SPEC C-6 names, transplanted onto a queue replay.
/// </summary>
public sealed class AppliesEventRegardlessOfIdempotencyConsumer(ISearchIndex index, IListingCatalog catalog) : IIngestionConsumer
{
    public async ValueTask<IngestionOutcome> ConsumeAsync(IngestionEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var payload = envelope.Payload;

        var listing = envelope.Type switch
        {
            ListingEventType.Published => new ListingDocument(
                payload.ListingId,
                payload.Title ?? "",
                payload.Description ?? "",
                payload.City ?? "",
                payload.PriceChf ?? 0,
                payload.Rooms ?? 0,
                ListingStatus.Active,
                payload.OwnerId ?? "",
                envelope.OccurredAt),

            ListingEventType.PriceChanged => Existing(payload.ListingId) with { PriceChf = payload.PriceChf ?? 0 },

            ListingEventType.Delisted => Existing(payload.ListingId) with { Status = ListingStatus.Delisted },

            _ => throw new InvalidOperationException($"Unrecognised event type '{envelope.Type}'."),
        };

        catalog.Upsert(listing);
        await index.IndexAsync(listing, cancellationToken).ConfigureAwait(false);

        return IngestionOutcome.Applied;
    }

    private ListingDocument Existing(string listingId) =>
        catalog.Find(listingId) ?? throw new InvalidOperationException($"No listing '{listingId}' to patch.");
}
