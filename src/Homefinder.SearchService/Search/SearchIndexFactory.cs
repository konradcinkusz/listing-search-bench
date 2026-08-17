using Homefinder.SearchService.Search.Elasticsearch;
using Homefinder.SearchService.Search.Fixtures;
using Microsoft.Extensions.Logging;

namespace Homefinder.SearchService.Search;

/// <summary>
/// The single assembly point for <see cref="ISearchIndex"/> — used identically by the
/// service and by the eval harness so the two can never drift (the same discipline
/// <c>WorkforceToolsFactory</c> applies in the worked example this repository mirrors).
/// </summary>
public static class SearchIndexFactory
{
    public static ISearchIndex Build(
        SearchIndexOptions options,
        IReadOnlyList<ListingDocument> seed,
        ILogger? logger = null,
        Func<ISearchIndex, ISearchIndex>? decorate = null)
    {
        ISearchIndex backend = options.Mode switch
        {
            SearchIndexMode.Elasticsearch when HasElasticsearchSettings(options) =>
                new ElasticsearchIndex(options),

            SearchIndexMode.Elasticsearch =>
                FallBackToFixture(logger, seed),

            _ => new InMemoryFixtureIndex(seed),
        };

        return Instrument(backend, options.MaxReadAttempts, decorate);
    }

    public static ISearchIndex Instrument(
        ISearchIndex backend, int maxReadAttempts, Func<ISearchIndex, ISearchIndex>? decorate = null)
    {
        var index = decorate is null ? backend : decorate(backend);
        return new InstrumentedSearchIndex(index, new IndexAttemptPolicy(maxReadAttempts));
    }

    private static bool HasElasticsearchSettings(SearchIndexOptions options) =>
        !string.IsNullOrWhiteSpace(options.ElasticsearchUri);

    private static InMemoryFixtureIndex FallBackToFixture(ILogger? logger, IReadOnlyList<ListingDocument> seed)
    {
        // P8: an optional dependency degrades to a working fallback rather than
        // failing startup. `SearchIndex:Mode=Elasticsearch` with no
        // `SearchIndex:ElasticsearchUri` is a misconfiguration, not a reason to
        // refuse to serve traffic — the in-memory fixture index is always available.
        logger?.LogWarning(
            "SearchIndex:Mode is Elasticsearch but SearchIndex:ElasticsearchUri is not set. "
            + "Falling back to the in-memory fixture index.");

        return new InMemoryFixtureIndex(seed);
    }
}
