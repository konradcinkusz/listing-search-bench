namespace ListingSearch.SearchService.Search;

/// <summary>
/// A listing's lifecycle state. <see cref="Active"/> is the only status any query's
/// default <see cref="SearchIndexFilter.AllowedStatuses"/> ever names — SPEC C-1.
/// </summary>
public enum ListingStatus
{
    Draft,
    Active,
    Delisted,
    Expired,
}

/// <summary>
/// A listing as the catalogue owns it. This is the write-side shape: what
/// <see cref="Ingestion.IngestionConsumer"/> turns an event into before calling
/// <see cref="ISearchIndex.IndexAsync"/>.
/// </summary>
public sealed record ListingDocument(
    string ListingId,
    string Title,
    string Description,
    string City,
    decimal PriceChf,
    decimal Rooms,
    ListingStatus Status,
    string OwnerId,
    DateTimeOffset ListedAt);

/// <summary>
/// The hard-filter shape every retrieval call carries. <see cref="AllowedStatuses"/>
/// is not user-suppliable — <see cref="Pipeline.Stages.FilterResolverStage"/> sets it,
/// always, to <c>[Active]</c> (SPEC §2.2). Everything else is the user's own filter.
/// </summary>
public sealed record SearchIndexFilter(
    decimal? MinPrice,
    decimal? MaxPrice,
    string? City,
    decimal? MinRooms,
    decimal? MaxRooms,
    IReadOnlyList<ListingStatus> AllowedStatuses)
{
    public bool Admits(ListingDocument listing) =>
        AllowedStatuses.Contains(listing.Status)
        && (MinPrice is not { } minPrice || listing.PriceChf >= minPrice)
        && (MaxPrice is not { } maxPrice || listing.PriceChf <= maxPrice)
        && (City is not { } city || string.Equals(listing.City, city, StringComparison.OrdinalIgnoreCase))
        && (MinRooms is not { } minRooms || listing.Rooms >= minRooms)
        && (MaxRooms is not { } maxRooms || listing.Rooms <= maxRooms);
}

/// <summary>
/// One hit from one index call. <see cref="DocumentId"/> is the backend's own internal
/// identifier — SPEC C-3 forbids it from ever reaching a response, so it exists on this
/// internal type and nowhere near <see cref="SearchResultItem"/>.
///
/// <see cref="ManipulationSignal"/> is set by the index at indexing time (once, not
/// per-query) when <c>RankingManipulationScanner</c> found instruction-shaped text in
/// the listing's own fields — carried here purely so <c>HybridRankerStage</c> can
/// report it (SPEC C-7's <c>ranking.manipulation_ignored</c>). Nothing downstream may
/// read it to change <see cref="RawScore"/>; that is the structural half of C-7.
/// </summary>
public sealed record IndexHit(
    string DocumentId,
    string ListingId,
    double RawScore,
    decimal PriceChf = 0,
    string? ManipulationSignal = null);

/// <summary>
/// The result of one <see cref="ISearchIndex"/> read. <see cref="Degraded"/> and
/// <see cref="DegradationKind"/> let an index implementation report a partial answer
/// without throwing — SPEC §7's "partial output with a note", at the boundary where
/// the note first becomes knowable. <see cref="RejectedListingIds"/> is what makes
/// <c>filter.rejected</c> (SPEC §2.3) possible: the ids of listings that matched the
/// query on relevance alone but were excluded by <c>filter</c> before scoring —
/// populated by <c>InMemoryFixtureIndex</c>, and left empty by
/// <c>ElasticsearchIndex</c>, whose single round trip has no cheap way to compute it
/// (docs/DEVIATIONS.md D-4).
/// </summary>
public sealed record IndexQueryResult(
    IReadOnlyList<IndexHit> Hits,
    bool Degraded = false,
    string? DegradationKind = null,
    IReadOnlyList<string>? RejectedListingIds = null)
{
    public static readonly IndexQueryResult Empty = new([]);

    public IReadOnlyList<string> Rejected => RejectedListingIds ?? [];
}

public sealed record IndexHealth(bool Healthy, string? Detail);

/// <summary>Which retrieval path(s) produced a result the caller actually sees.</summary>
public enum ResultAttribution
{
    Lexical,
    Vector,
    Both,
}

/// <summary>
/// The public response shape. Deliberately narrow: no <see cref="IndexHit.DocumentId"/>,
/// no raw score, no embedding vector — only what SPEC C-3 allows a caller to see, plus
/// the normalised, rounded ranking value C-5 requires be recoverable.
/// </summary>
public sealed record SearchResultItem(
    string ListingId,
    int Rank,
    ResultAttribution Source,
    double RankingScore);

public enum SearchOutcome
{
    Completed,
    Degraded,
}

public sealed record DegradationNote(string Stage, string Kind);

public sealed record SearchResponse(
    IReadOnlyList<SearchResultItem> Results,
    SearchOutcome Outcome,
    IReadOnlyList<DegradationNote> Degradations);

public enum SortOrder
{
    Relevance,
    PriceAscending,
    PriceDescending,
}

// SoftMaxPrice is a soft ceiling, distinct from MaxPrice: a listing above it is never
// excluded (SPEC B-6 — "around CHF 1,000,000" is a preference, "under CHF 1,000,000"
// is a filter), only ranked lower. MaxPrice is what a caller sets for the second
// sentence; SoftMaxPrice is what it sets for the first.
public sealed record SearchRequest(
    string QueryText,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    string? City = null,
    decimal? MinRooms = null,
    decimal? MaxRooms = null,
    decimal? SoftMaxPrice = null,
    SortOrder Sort = SortOrder.Relevance,
    int Top = 10);
