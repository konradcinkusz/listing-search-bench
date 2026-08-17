namespace ListingSearch.Service.Ingestion;

public enum ListingEventType
{
    Published,
    PriceChanged,
    Delisted,
}

/// <summary>
/// The fields an ingestion event may carry. Which are required depends on
/// <see cref="IngestionEnvelope.Type"/> — <c>IngestionConsumer.RequireFields</c> is
/// the one place that mapping lives.
/// </summary>
public sealed record ListingEventPayload(
    string ListingId,
    string? Title = null,
    string? Description = null,
    string? City = null,
    decimal? PriceChf = null,
    decimal? Rooms = null,
    string? OwnerId = null);

/// <summary>
/// One event off the ingestion queue — <c>listing.published</c>,
/// <c>listing.price_changed</c> or <c>listing.delisted</c> (SPEC §1). Reachable only
/// through <see cref="IIngestionConsumer"/>; there is deliberately no HTTP route that
/// constructs one directly (SPEC O-1).
/// </summary>
public sealed record IngestionEnvelope(string EventId, ListingEventType Type, ListingEventPayload Payload, DateTimeOffset OccurredAt);

public enum IngestionOutcome
{
    Applied,
    DuplicateIgnored,
    Failed,
}
