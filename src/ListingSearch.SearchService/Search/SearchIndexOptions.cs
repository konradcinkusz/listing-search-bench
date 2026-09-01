namespace ListingSearch.SearchService.Search;

public enum SearchIndexMode
{
    Fixture,
    Elasticsearch,
}

/// <summary>
/// Bound from the <c>SearchIndex</c> configuration section. <see cref="Mode"/> defaults
/// to <see cref="SearchIndexMode.Fixture"/> — ADR-0002: a fresh clone runs with no
/// credentials at all, and reaching for a real cluster is something a developer opts
/// into, never something a missing setting falls back out of silently.
/// </summary>
public sealed class SearchIndexOptions
{
    public const string SectionName = "SearchIndex";

    public SearchIndexMode Mode { get; set; } = SearchIndexMode.Fixture;

    public string FixtureName { get; set; } = "zurich-catalogue";

    public int MaxReadAttempts { get; set; } = 2;

    public string? ElasticsearchUri { get; set; }

    public string? ElasticsearchApiKey { get; set; }

    public string ElasticsearchIndexName { get; set; } = "listing-search-listings";
}
