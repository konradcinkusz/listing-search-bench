using System.Diagnostics;
using ListingSearch.SearchService.Pipeline;
using ListingSearch.SearchService.Search;
using ListingSearch.SearchService.Telemetry;

namespace ListingSearch.SearchService.Ingestion;

public interface IIngestionConsumer
{
    ValueTask<IngestionOutcome> ConsumeAsync(IngestionEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// The only write path into <see cref="ISearchIndex"/> — SPEC §2.1, O-1. There is
/// deliberately no HTTP route that reaches <see cref="ISearchIndex.IndexAsync"/> or
/// <see cref="ISearchIndex.DeleteAsync"/> any other way: a convenience endpoint would
/// hand every adversarial scenario a way around the exact thing this specification
/// tests.
/// </summary>
public sealed class IngestionConsumer(
    ISearchIndex index,
    IListingCatalog catalog,
    IEventIdempotencyStore idempotency,
    IPendingEventBuffer pending,
    IDeadLetterSink deadLetters)
    : IIngestionConsumer
{
    public async ValueTask<IngestionOutcome> ConsumeAsync(IngestionEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        using var activity = SearchDiagnostics.Source.StartActivity("ingestion_consume", ActivityKind.Consumer);
        activity?.SetTag(SearchDiagnostics.Attributes.IngestionEventId, envelope.EventId);
        activity?.SetTag(SearchDiagnostics.Attributes.IngestionEventType, envelope.Type.ToString());
        activity?.SetTag(SearchDiagnostics.Attributes.IngestionListingId, envelope.Payload.ListingId);

        if (!idempotency.TryReserve(envelope.EventId))
        {
            SearchTurnContext.EmitEvent(
                SearchDiagnostics.Events.IngestionDuplicateIgnored,
                (SearchDiagnostics.Attributes.IngestionEventId, envelope.EventId));
            return IngestionOutcome.DuplicateIgnored;
        }

        return await ApplyAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies one already-<see cref="IEventIdempotencyStore.TryReserve"/>d envelope —
    /// called once from <see cref="ConsumeAsync"/> for a freshly received event, and
    /// again, recursively, from <see cref="ReplayPendingAsync"/> for each envelope a
    /// <c>published</c> event's arrival unblocks. Replayed envelopes were reserved when
    /// they were first deferred, so this never reserves twice for one <c>event_id</c>.
    /// </summary>
    private async ValueTask<IngestionOutcome> ApplyAsync(IngestionEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = envelope.Payload;

        if (envelope.Type is ListingEventType.PriceChanged or ListingEventType.Delisted
            && catalog.Find(payload.ListingId) is null)
        {
            // A price_changed missing its own price is malformed regardless of
            // whether the listing exists yet — deferring it would only delay a
            // failure that replaying the eventual published event cannot fix.
            if (envelope.Type == ListingEventType.PriceChanged && payload.PriceChf is not ({ } and >= 0))
            {
                return Fail(envelope);
            }

            return Defer(envelope);
        }

        try
        {
            var listing = Resolve(envelope);
            catalog.Upsert(listing);
            await index.IndexAsync(listing, cancellationToken).ConfigureAwait(false);

            SearchTurnContext.EmitEvent(
                SearchDiagnostics.Events.IngestionApplied,
                (SearchDiagnostics.Attributes.IngestionEventId, envelope.EventId),
                (SearchDiagnostics.Attributes.IngestionListingId, payload.ListingId));

            if (envelope.Type == ListingEventType.Published)
            {
                await ReplayPendingAsync(payload.ListingId, cancellationToken).ConfigureAwait(false);
            }

            return IngestionOutcome.Applied;
        }
        catch (InvalidOperationException)
        {
            return Fail(envelope);
        }
    }

    private IngestionOutcome Fail(IngestionEnvelope envelope)
    {
        // SPEC §7.2: a failed apply releases the reservation, so a corrected replay
        // of the same event_id is not mistaken for a duplicate.
        idempotency.Release(envelope.EventId);

        SearchTurnContext.EmitEvent(
            SearchDiagnostics.Events.IngestionFailed,
            (SearchDiagnostics.Attributes.IngestionEventId, envelope.EventId));

        return IngestionOutcome.Failed;
    }

    private IngestionOutcome Defer(IngestionEnvelope envelope)
    {
        if (pending.TryDefer(envelope.Payload.ListingId, envelope))
        {
            SearchTurnContext.EmitEvent(
                SearchDiagnostics.Events.IngestionDeferred,
                (SearchDiagnostics.Attributes.IngestionEventId, envelope.EventId),
                (SearchDiagnostics.Attributes.IngestionListingId, envelope.Payload.ListingId));
            return IngestionOutcome.Deferred;
        }

        // The pending buffer for this listing is already full — no published event
        // has arrived after maxPerListing attempts. Giving up is the honest answer;
        // buffering forever is not (SPEC §7.2, B-12).
        idempotency.Release(envelope.EventId);

        const string reason = "no listing.published seen before this listing's pending buffer filled up";
        deadLetters.Publish(new DeadLetteredEvent(envelope.EventId, envelope.Payload.ListingId, reason, envelope.OccurredAt));

        SearchTurnContext.EmitEvent(
            SearchDiagnostics.Events.IngestionDeadLettered,
            (SearchDiagnostics.Attributes.IngestionEventId, envelope.EventId),
            (SearchDiagnostics.Attributes.IngestionListingId, envelope.Payload.ListingId),
            (SearchDiagnostics.Attributes.IngestionDeadLetterReason, reason));

        return IngestionOutcome.DeadLettered;
    }

    private async ValueTask ReplayPendingAsync(string listingId, CancellationToken cancellationToken)
    {
        foreach (var buffered in pending.DrainFor(listingId))
        {
            await ApplyAsync(buffered, cancellationToken).ConfigureAwait(false);
        }
    }

    private ListingDocument Resolve(IngestionEnvelope envelope)
    {
        var payload = envelope.Payload;

        return envelope.Type switch
        {
            ListingEventType.Published => new ListingDocument(
                payload.ListingId,
                Require(envelope, payload.Title, nameof(payload.Title)),
                Require(envelope, payload.Description, nameof(payload.Description)),
                Require(envelope, payload.City, nameof(payload.City)),
                RequirePrice(envelope, payload.PriceChf),
                RequireRooms(envelope, payload.Rooms),
                ListingStatus.Active,
                Require(envelope, payload.OwnerId, nameof(payload.OwnerId)),
                envelope.OccurredAt),

            ListingEventType.PriceChanged => Existing(envelope) with
            {
                PriceChf = RequirePrice(envelope, payload.PriceChf),
            },

            ListingEventType.Delisted => Existing(envelope) with
            {
                Status = ListingStatus.Delisted,
            },

            _ => throw new InvalidOperationException($"Unrecognised ingestion event type '{envelope.Type}'."),
        };
    }

    private ListingDocument Existing(IngestionEnvelope envelope) =>
        catalog.Find(envelope.Payload.ListingId)
            ?? throw new InvalidOperationException(
                $"Event '{envelope.EventId}' ({envelope.Type}) names listing "
                + $"'{envelope.Payload.ListingId}', which this catalogue has never seen a "
                + "listing.published event for. A price change or a delisting on an unknown "
                + "listing is a malformed event, not a listing to create from a partial payload.");

    private static string Require(IngestionEnvelope envelope, string? value, string field) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Event '{envelope.EventId}' ({envelope.Type}) is missing required field '{field}'.");

    private static decimal RequirePrice(IngestionEnvelope envelope, decimal? value) =>
        value is { } price and >= 0
            ? price
            : throw new InvalidOperationException(
                $"Event '{envelope.EventId}' ({envelope.Type}) is missing a valid price_chf.");

    private static decimal RequireRooms(IngestionEnvelope envelope, decimal? value) =>
        value is { } rooms and > 0
            ? rooms
            : throw new InvalidOperationException(
                $"Event '{envelope.EventId}' ({envelope.Type}) is missing a valid rooms count.");
}
