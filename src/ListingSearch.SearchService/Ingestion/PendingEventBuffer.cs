namespace ListingSearch.SearchService.Ingestion;

/// <summary>
/// Where a <c>price_changed</c> or <c>delisted</c> event waits when it names a listing
/// <see cref="IngestionConsumer"/> has not seen a <c>published</c> event for yet — SPEC
/// §7.2, B-12. Bounded per listing: a real queue does not guarantee ordering across
/// partitions, but it does not buffer forever either, and neither does this.
/// </summary>
public interface IPendingEventBuffer
{
    /// <summary>
    /// Buffers <paramref name="envelope"/> for <paramref name="listingId"/>. Returns
    /// false if that listing's buffer is already at capacity — the caller's cue to
    /// dead-letter instead of buffering indefinitely.
    /// </summary>
    bool TryDefer(string listingId, IngestionEnvelope envelope);

    /// <summary>Removes and returns every buffered envelope for <paramref name="listingId"/>, in the order they were deferred.</summary>
    IReadOnlyList<IngestionEnvelope> DrainFor(string listingId);
}

/// <summary>
/// The in-memory default (ADR-0002's zero-credential pattern, applied here). A single
/// <see cref="_gate"/> lock is a deliberate simplification, not a claim of production
/// throughput — the same honesty <c>InMemoryEventIdempotencyStore</c> and
/// <c>InMemoryListingCatalog</c> already state for this POC's in-process state.
/// </summary>
public sealed class InMemoryPendingEventBuffer(int maxPerListing = 3) : IPendingEventBuffer
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<IngestionEnvelope>> _pending = new(StringComparer.Ordinal);

    public bool TryDefer(string listingId, IngestionEnvelope envelope)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(listingId, out var queue))
            {
                queue = [];
                _pending[listingId] = queue;
            }

            if (queue.Count >= maxPerListing)
            {
                return false;
            }

            queue.Add(envelope);
            return true;
        }
    }

    public IReadOnlyList<IngestionEnvelope> DrainFor(string listingId)
    {
        lock (_gate)
        {
            if (!_pending.Remove(listingId, out var queue))
            {
                return [];
            }

            return queue;
        }
    }
}
