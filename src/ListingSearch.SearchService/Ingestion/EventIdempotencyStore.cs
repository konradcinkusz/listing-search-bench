using System.Collections.Concurrent;

namespace ListingSearch.SearchService.Ingestion;

/// <summary>
/// The idempotency registry SPEC C-6 requires: an event is applied at most once per
/// <c>event_id</c>. <see cref="TryReserve"/> is the atomic gate — the same
/// reserve-then-commit-or-release shape <c>ConfirmationTokenStore</c> uses in the
/// worked example this repository mirrors, applied here to a replayed queue message
/// rather than a human approval.
/// </summary>
public interface IEventIdempotencyStore
{
    /// <summary>Atomically claims <paramref name="eventId"/>. False means it was already applied or is in flight.</summary>
    bool TryReserve(string eventId);

    /// <summary>
    /// Releases a reservation whose apply failed, so a corrected replay of the same
    /// <c>event_id</c> is not mistaken for a duplicate (SPEC §7.2).
    /// </summary>
    void Release(string eventId);
}

public sealed class InMemoryEventIdempotencyStore : IEventIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> _reserved = new(StringComparer.Ordinal);

    public bool TryReserve(string eventId) => _reserved.TryAdd(eventId, 0);

    public void Release(string eventId) => _reserved.TryRemove(eventId, out _);
}
