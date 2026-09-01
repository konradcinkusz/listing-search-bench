using System.Diagnostics;
using ListingSearch.SearchService.Telemetry;

namespace ListingSearch.SearchService.Search;

/// <summary>
/// Wraps an <see cref="ISearchIndex"/> to add tracing and attempt-bounded retries —
/// never to duplicate business logic. Every interface method is a thin call into
/// <see cref="TraceReadAsync"/> or <see cref="TraceWriteAsync"/>.
///
/// <para>
/// Deliberately does <b>not</b> re-check a returned hit against <c>filter</c>. A
/// filter built too permissively by a retrieval stage (SPEC §2.2 — each stage builds
/// its own) must be observable as a real leak, not silently absorbed here: that is
/// what makes <c>skip-delisted-check-on-vector-path</c> ([§8.6](../../docs/SPEC.md#86-proving-the-suite-can-fail))
/// a mutation the suite can actually catch, rather than one this decorator would
/// quietly heal regardless of what broke upstream. The independent check this service
/// does perform lives at the response boundary instead —
/// <c>ResponseAssemblerStage</c> — where it protects a different constraint (C-3, no
/// internal identifier ever reaches a caller) that no planned mutation targets, so
/// defending it in two places costs nothing.
/// </para>
/// </summary>
public sealed class InstrumentedSearchIndex(ISearchIndex inner, IIndexAttemptPolicy attempts) : ISearchIndex
{
    public InstrumentedSearchIndex(ISearchIndex inner)
        : this(inner, new IndexAttemptPolicy(maxReadAttempts: 1))
    {
    }

    public ValueTask<IndexQueryResult> QueryAsync(
        SearchIndexFilter filter, IReadOnlyList<string> tokens, int topN, CancellationToken cancellationToken = default) =>
        TraceReadAsync(
            SearchIndexOperationCatalog.Query,
            filter,
            () => inner.QueryAsync(filter, tokens, topN, cancellationToken));

    public ValueTask<IndexQueryResult> VectorQueryAsync(
        SearchIndexFilter filter, IReadOnlyList<double> queryEmbedding, int topN, CancellationToken cancellationToken = default) =>
        TraceReadAsync(
            SearchIndexOperationCatalog.VectorQuery,
            filter,
            () => inner.VectorQueryAsync(filter, queryEmbedding, topN, cancellationToken));

    public async ValueTask IndexAsync(ListingDocument document, CancellationToken cancellationToken = default) =>
        await TraceWriteAsync(
            SearchIndexOperationCatalog.Index,
            document.ListingId,
            () => inner.IndexAsync(document, cancellationToken)).ConfigureAwait(false);

    public async ValueTask DeleteAsync(string listingId, CancellationToken cancellationToken = default) =>
        await TraceWriteAsync(
            SearchIndexOperationCatalog.Delete,
            listingId,
            () => inner.DeleteAsync(listingId, cancellationToken)).ConfigureAwait(false);

    public async ValueTask<IndexHealth> HealthAsync(CancellationToken cancellationToken = default)
    {
        using var activity = SearchDiagnostics.Source.StartActivity(
            $"search_index {SearchIndexOperationCatalog.Health}", ActivityKind.Client);
        activity?.SetTag(SearchDiagnostics.Attributes.IndexOperation, SearchIndexOperationCatalog.Health);

        return await inner.HealthAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IndexQueryResult> TraceReadAsync(
        string operation, SearchIndexFilter filter, Func<ValueTask<IndexQueryResult>> call)
    {
        using var activity = SearchDiagnostics.Source.StartActivity($"search_index {operation}", ActivityKind.Client);
        activity?.SetTag(SearchDiagnostics.Attributes.IndexOperation, operation);
        activity?.SetTag(SearchDiagnostics.Attributes.IndexKind, "read");

        var maxAttempts = attempts.MaxAttempts(operation);
        IndexQueryResult result = IndexQueryResult.Empty;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            result = await call().ConfigureAwait(false);

            activity?.AddEvent(new ActivityEvent(
                SearchDiagnostics.Events.Attempt,
                tags: new ActivityTagsCollection
                {
                    [SearchDiagnostics.Attributes.AttemptNumber] = attempt,
                    [SearchDiagnostics.Attributes.AttemptOutcome] = result.Degraded ? result.DegradationKind ?? "degraded" : "success",
                }));

            if (!attempts.IsWorthRetrying(result.Degraded, result.DegradationKind) || attempt == maxAttempts)
            {
                break;
            }
        }

        activity?.SetTag(SearchDiagnostics.Attributes.IndexCandidateCount, result.Hits.Count);
        activity?.SetTag(SearchDiagnostics.Attributes.IndexResultIds, string.Join(';', result.Hits.Select(h => h.ListingId)));
        activity?.SetTag(SearchDiagnostics.Attributes.FilterCity, filter.City);

        if (result.Degraded)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.DegradationKind);
        }

        return result;
    }

    private static async ValueTask TraceWriteAsync(string operation, string listingId, Func<ValueTask> call)
    {
        using var activity = SearchDiagnostics.Source.StartActivity($"search_index {operation}", ActivityKind.Client);
        activity?.SetTag(SearchDiagnostics.Attributes.IndexOperation, operation);
        activity?.SetTag(SearchDiagnostics.Attributes.IndexKind, "write");
        activity?.SetTag(SearchDiagnostics.Attributes.IndexResultIds, listingId);

        // Writes get exactly one attempt (IndexAttemptPolicy), so there is no retry
        // loop to log here — the single `attempt` event still lets a scenario assert
        // `call_attempts` on a write the same way it does on a read.
        activity?.AddEvent(new ActivityEvent(
            SearchDiagnostics.Events.Attempt,
            tags: new ActivityTagsCollection
            {
                [SearchDiagnostics.Attributes.AttemptNumber] = 1,
                [SearchDiagnostics.Attributes.AttemptOutcome] = "success",
            }));

        await call().ConfigureAwait(false);
    }
}
