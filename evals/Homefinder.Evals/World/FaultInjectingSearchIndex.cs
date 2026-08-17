using System.Collections.Concurrent;
using Homefinder.Evals.Scenarios;
using Homefinder.SearchService.Search;
using Homefinder.SearchService.Telemetry;

namespace Homefinder.Evals.World;

/// <summary>
/// Wraps a real <see cref="ISearchIndex"/> and injects the failures
/// <c>evals/scenarios/degradation/*.yaml</c> declares — timeouts, shard
/// unavailability, malformed embeddings — at the tool seam, below the OTel
/// instrumentation decorator, so a fault still produces a real span with real attempt
/// events (mirrors <c>FaultInjectingWorkforceTools</c> in the worked example this
/// repository mirrors). Per SPEC §8.1, an injected timeout is declared, not slept
/// through: the fault returns immediately, marked degraded, rather than the harness
/// spending wall-clock time honouring a delay nobody is measuring.
/// </summary>
public sealed class FaultInjectingSearchIndex(ISearchIndex inner, IReadOnlyDictionary<string, IndexBehaviour> behaviours) : ISearchIndex
{
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

    public async ValueTask<IndexQueryResult> QueryAsync(
        SearchIndexFilter filter, IReadOnlyList<string> tokens, int topN, CancellationToken cancellationToken = default)
    {
        if (Faulted(SearchIndexOperationCatalog.Query) is { } fault)
        {
            return fault;
        }

        return await inner.QueryAsync(filter, tokens, topN, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IndexQueryResult> VectorQueryAsync(
        SearchIndexFilter filter, IReadOnlyList<double> queryEmbedding, int topN, CancellationToken cancellationToken = default)
    {
        if (Faulted(SearchIndexOperationCatalog.VectorQuery) is { } fault)
        {
            return fault;
        }

        return await inner.VectorQueryAsync(filter, queryEmbedding, topN, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask IndexAsync(ListingDocument document, CancellationToken cancellationToken = default) =>
        inner.IndexAsync(document, cancellationToken);

    public ValueTask DeleteAsync(string listingId, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(listingId, cancellationToken);

    public ValueTask<IndexHealth> HealthAsync(CancellationToken cancellationToken = default) =>
        inner.HealthAsync(cancellationToken);

    private IndexQueryResult? Faulted(string operation)
    {
        var callNumber = _calls.AddOrUpdate(operation, 1, (_, count) => count + 1);

        if (!behaviours.TryGetValue(operation, out var behaviour)
            || string.Equals(behaviour.Outcome, "success", StringComparison.Ordinal))
        {
            return null;
        }

        if (callNumber < (behaviour.AfterCalls ?? 1))
        {
            return null;
        }

        var kind = behaviour.Outcome switch
        {
            "timeout" => SearchDiagnostics.DegradationKinds.Timeout,
            "shard_unavailable" => SearchDiagnostics.DegradationKinds.ShardUnavailable,
            "malformed_embedding" => SearchDiagnostics.DegradationKinds.MalformedEmbedding,
            "empty" => SearchDiagnostics.DegradationKinds.Empty,
            _ => throw new InvalidOperationException(
                $"Unrecognised index_behaviour outcome '{behaviour.Outcome}' for operation '{operation}'. "
                + "Valid values: success, timeout, shard_unavailable, malformed_embedding, empty."),
        };

        return new IndexQueryResult([], Degraded: true, DegradationKind: kind);
    }
}
