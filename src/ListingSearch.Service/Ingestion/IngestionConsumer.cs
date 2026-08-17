using System.Diagnostics;
using ListingSearch.Service.Pipeline;
using ListingSearch.Service.Search;
using ListingSearch.Service.Telemetry;

namespace ListingSearch.Service.Ingestion;

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
public sealed class IngestionConsumer(ISearchIndex index, IListingCatalog catalog, IEventIdempotencyStore idempotency)
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

        try
        {
            var listing = Resolve(envelope);
            catalog.Upsert(listing);
            await index.IndexAsync(listing, cancellationToken).ConfigureAwait(false);

            SearchTurnContext.EmitEvent(
                SearchDiagnostics.Events.IngestionApplied,
                (SearchDiagnostics.Attributes.IngestionEventId, envelope.EventId),
                (SearchDiagnostics.Attributes.IngestionListingId, envelope.Payload.ListingId));

            return IngestionOutcome.Applied;
        }
        catch (InvalidOperationException)
        {
            // SPEC §7.2: a failed apply releases the reservation, so a corrected
            // replay of the same event_id is not mistaken for a duplicate.
            idempotency.Release(envelope.EventId);

            SearchTurnContext.EmitEvent(
                SearchDiagnostics.Events.IngestionFailed,
                (SearchDiagnostics.Attributes.IngestionEventId, envelope.EventId));

            return IngestionOutcome.Failed;
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
