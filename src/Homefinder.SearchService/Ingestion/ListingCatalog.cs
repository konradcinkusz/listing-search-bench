using System.Collections.Concurrent;
using Homefinder.SearchService.Search;

namespace Homefinder.SearchService.Ingestion;

/// <summary>
/// The system of record <see cref="IngestionConsumer"/> reads and writes — the role
/// SQL Server plays in the JD this repository answers (README, "where the repository
/// answers the JD"). The search index is a denormalised projection built from this,
/// never the other way round: <see cref="ISearchIndex"/> has no read-by-id method
/// (five methods, ADR-0005), so a partial update like <c>listing.price_changed</c> reads
/// the current row here, patches it, and pushes the whole resulting document to the
/// index — the same shape a real reindex-from-source-of-truth pipeline has.
/// </summary>
public interface IListingCatalog
{
    ListingDocument? Find(string listingId);

    void Upsert(ListingDocument listing);
}

public sealed class InMemoryListingCatalog : IListingCatalog
{
    private readonly ConcurrentDictionary<string, ListingDocument> _byId = new(StringComparer.Ordinal);

    public InMemoryListingCatalog()
    {
    }

    public InMemoryListingCatalog(IEnumerable<ListingDocument> seed)
    {
        foreach (var listing in seed)
        {
            _byId[listing.ListingId] = listing;
        }
    }

    public ListingDocument? Find(string listingId) => _byId.GetValueOrDefault(listingId);

    public void Upsert(ListingDocument listing) => _byId[listing.ListingId] = listing;
}
