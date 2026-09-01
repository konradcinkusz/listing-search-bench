namespace ListingSearch.SearchService.Search;

/// <summary>
/// The one internal interface the pipeline reaches a retrieval backend through.
/// Implementations: <c>InMemoryFixtureIndex</c> (the demonstrated path, zero
/// credentials — ADR-0002) and <c>ElasticsearchIndex</c> (a real cluster, dev-only,
/// never constructed by anything CI runs).
///
/// Extensibility is interface plus a registration line (P10). There is no base class
/// to derive from and no framework to satisfy.
/// </summary>
public interface ISearchIndex
{
    ValueTask<IndexQueryResult> QueryAsync(
        SearchIndexFilter filter,
        IReadOnlyList<string> tokens,
        int topN,
        CancellationToken cancellationToken = default);

    ValueTask<IndexQueryResult> VectorQueryAsync(
        SearchIndexFilter filter,
        IReadOnlyList<double> queryEmbedding,
        int topN,
        CancellationToken cancellationToken = default);

    ValueTask IndexAsync(ListingDocument document, CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(string listingId, CancellationToken cancellationToken = default);

    ValueTask<IndexHealth> HealthAsync(CancellationToken cancellationToken = default);
}

public enum SearchIndexOperationKind
{
    Read,
    Write,
}

/// <summary>
/// The operation catalogue — read/write classification, as data rather than as a
/// naming convention. A rule like <c>name.StartsWith("Index")</c> would silently
/// classify every future operation as a write (or a read) until somebody remembered
/// to update it — the same failure shape <c>WorkforceToolCatalog</c> in the worked
/// example this repository mirrors was written to prevent.
/// </summary>
public static class SearchIndexOperationCatalog
{
    public const string Query = "query";
    public const string VectorQuery = "vector_query";
    public const string Index = "index";
    public const string Delete = "delete";
    public const string Health = "health";

    private static readonly Dictionary<string, SearchIndexOperationKind> KindByName = new(StringComparer.Ordinal)
    {
        [Query] = SearchIndexOperationKind.Read,
        [VectorQuery] = SearchIndexOperationKind.Read,
        [Index] = SearchIndexOperationKind.Write,
        [Delete] = SearchIndexOperationKind.Write,
        [Health] = SearchIndexOperationKind.Read,
    };

    public static SearchIndexOperationKind KindOf(string operation) =>
        KindByName.TryGetValue(operation, out var kind)
            ? kind
            : throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation,
                "Unknown index operation. Every operation must be classified read or write "
                + "in SearchIndexOperationCatalog before it can be called.");

    public static bool IsWrite(string operation) => KindOf(operation) == SearchIndexOperationKind.Write;
}
