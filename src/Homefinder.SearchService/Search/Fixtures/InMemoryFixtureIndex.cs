using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Homefinder.SearchService.Pipeline;
using Homefinder.SearchService.Pipeline.Embedding;

namespace Homefinder.SearchService.Search.Fixtures;

/// <summary>
/// The default, zero-credential <see cref="ISearchIndex"/> — ADR-0002. Every query and
/// vector-query honours exactly the <see cref="SearchIndexFilter"/> it was given and
/// nothing more: it has no opinion of its own about which statuses are safe to return,
/// because a real Elasticsearch index does not either. That is a deliberate property,
/// not an oversight — see <c>InstrumentedSearchIndex</c>'s doc comment for why this
/// index re-checking its own filter would make the mutation pass unfalsifiable.
/// </summary>
public sealed class InMemoryFixtureIndex : ISearchIndex
{
    private readonly ConcurrentDictionary<string, StoredDocument> _documents = new(StringComparer.Ordinal);

    public InMemoryFixtureIndex()
    {
    }

    public InMemoryFixtureIndex(IEnumerable<ListingDocument> seed)
    {
        foreach (var listing in seed)
        {
            _documents[listing.ListingId] = StoredDocument.From(listing);
        }
    }

    public int Count => _documents.Count;

    public ValueTask<IndexQueryResult> QueryAsync(
        SearchIndexFilter filter, IReadOnlyList<string> tokens, int topN, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ScoreAndFilter(
            filter,
            topN,
            doc => LexicalScorer.Score(tokens, doc.TitleTokens, doc.DescriptionTokens)));

    public ValueTask<IndexQueryResult> VectorQueryAsync(
        SearchIndexFilter filter, IReadOnlyList<double> queryEmbedding, int topN, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ScoreAndFilter(
            filter,
            topN,
            doc => DeterministicTextEmbedding.CosineSimilarity(queryEmbedding, doc.Embedding)));

    /// <summary>
    /// Scores every stored document once, then splits the matches (score &gt; 0) into
    /// admitted (kept, ranked, truncated to <paramref name="topN"/>) and rejected
    /// (excluded by <paramref name="filter"/>, reported so a caller can emit
    /// <c>filter.rejected</c> — SPEC §2.3). A document that neither matches the query
    /// nor passes the filter is silently absent from both lists: it was never a
    /// candidate on relevance grounds, so its exclusion is not a filter event, it is
    /// nothing happening.
    /// </summary>
    private IndexQueryResult ScoreAndFilter(SearchIndexFilter filter, int topN, Func<StoredDocument, double> score)
    {
        var admitted = new List<(StoredDocument Doc, double Score)>();
        var rejected = new List<string>();

        foreach (var doc in _documents.Values)
        {
            var value = score(doc);

            if (value <= 0)
            {
                continue;
            }

            if (filter.Admits(doc.Listing))
            {
                admitted.Add((doc, value));
            }
            else
            {
                rejected.Add(doc.Listing.ListingId);
            }
        }

        var hits = admitted
            .OrderByDescending(pair => pair.Score)
            .ThenBy(pair => pair.Doc.Listing.ListingId, StringComparer.Ordinal)
            .Take(Math.Max(0, topN))
            .Select(pair => new IndexHit(
                pair.Doc.DocumentId,
                pair.Doc.Listing.ListingId,
                pair.Score,
                pair.Doc.Listing.PriceChf,
                pair.Doc.ManipulationSignal))
            .ToList();

        return new IndexQueryResult(hits, RejectedListingIds: rejected);
    }

    public ValueTask IndexAsync(ListingDocument document, CancellationToken cancellationToken = default)
    {
        _documents[document.ListingId] = StoredDocument.From(document);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string listingId, CancellationToken cancellationToken = default)
    {
        _documents.TryRemove(listingId, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IndexHealth> HealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new IndexHealth(true, null));

    private sealed record StoredDocument(
        ListingDocument Listing,
        string DocumentId,
        IReadOnlyList<string> TitleTokens,
        IReadOnlyList<string> DescriptionTokens,
        double[] Embedding,
        string? ManipulationSignal)
    {
        public static StoredDocument From(ListingDocument listing)
        {
            var titleTokens = TextTokenizer.Tokenize(listing.Title);
            var descriptionTokens = TextTokenizer.Tokenize(listing.Description);
            var embedding = DeterministicTextEmbedding.Compute([.. titleTokens, .. descriptionTokens]);

            // Scanned once, at index time, not on every query — SPEC C-7's finding is
            // a property of the listing's own text, not of any particular search.
            var signal = RankingManipulationScanner.Scan(listing.ListingId, $"{listing.Title} {listing.Description}")?.Signal;

            return new StoredDocument(
                listing, InternalDocumentId(listing.ListingId), titleTokens, descriptionTokens, embedding, signal);
        }
    }

    /// <summary>
    /// A stable, fixture-independent stand-in for "the backend's own internal id" —
    /// the value SPEC C-3 forbids from ever reaching a response. Derived from the
    /// listing id by a fixed hash so it looks nothing like one (<c>esdoc-xxxxxxxx</c>,
    /// never <c>lst-xxxx</c>), which is what makes a leak of this value into a
    /// response distinguishable, in a test, from a leak of the public id.
    /// </summary>
    private static string InternalDocumentId(string listingId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(listingId));
        return "esdoc-" + Convert.ToHexStringLower(hash)[..8];
    }
}
