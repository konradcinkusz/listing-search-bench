using System.Collections.Concurrent;

namespace Homefinder.SearchService.Ingestion;

/// <summary>
/// Where an event <see cref="IngestionConsumer"/> gave up on goes — the seam a real
/// dead-letter topic would sit behind, the same pattern <c>IEmbeddingProvider</c>
/// applies to a real embedding model (D-1): nothing downstream of this interface knows
/// or cares whether the implementation is in-memory or a real queue.
/// </summary>
public interface IDeadLetterSink
{
    void Publish(DeadLetteredEvent entry);
}

/// <summary>The in-memory default — holds entries for inspection (tests, an operator endpoint someday) rather than discarding them.</summary>
public sealed class InMemoryDeadLetterSink : IDeadLetterSink
{
    private readonly ConcurrentQueue<DeadLetteredEvent> _entries = new();

    public void Publish(DeadLetteredEvent entry) => _entries.Enqueue(entry);

    public IReadOnlyList<DeadLetteredEvent> Entries => [.. _entries];
}
