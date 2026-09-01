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

    /// <summary>
    /// A well-formed <c>price_changed</c> or <c>delisted</c> event named a listing this
    /// consumer has never seen a <c>published</c> event for — buffered, not failed,
    /// because a transport that does not guarantee ordering can legitimately deliver
    /// these out of sequence. Replayed automatically once the matching
    /// <c>published</c> event arrives (SPEC §7.2, B-12).
    /// </summary>
    Deferred,

    /// <summary>
    /// A deferred event whose listing's pending buffer filled up before a
    /// <c>published</c> event ever arrived for it — given up on and handed to
    /// <see cref="IDeadLetterSink"/> rather than buffered forever.
    /// </summary>
    DeadLettered,
}

/// <summary>One event <see cref="IDeadLetterSink"/> received because it could not be resolved.</summary>
public sealed record DeadLetteredEvent(string EventId, string ListingId, string Reason, DateTimeOffset OccurredAt);
