using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Elastic.Transport;
using ListingSearch.Service.Pipeline.Embedding;

namespace ListingSearch.Service.Search.Elasticsearch;

/// <summary>
/// The real backend, behind the same seam as <c>InMemoryFixtureIndex</c> — ADR-0002 and
/// ADR-0005. Dev-only: no CI job constructs this type, because no CI job has a cluster
/// to point it at, so this file's correctness is a documented, dated gap
/// (docs/DEVIATIONS.md D-4) rather than a claim backed by a passing test. It is
/// written from the client SDK's own documented shapes, the same honesty
/// <c>agent-eval-bench</c> states for its MCP adapter.
/// </summary>
public sealed class ElasticsearchIndex : ISearchIndex
{
    private static readonly string[] MatchFields = ["title^2", "description"];

    private readonly ElasticsearchClient _client;
    private readonly string _indexName;
    private readonly IEmbeddingProvider _embeddingProvider;

    public ElasticsearchIndex(SearchIndexOptions options, IEmbeddingProvider? embeddingProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ElasticsearchUri))
        {
            throw new InvalidOperationException(
                "ElasticsearchIndex requires SearchIndex:ElasticsearchUri. Use SearchIndexFactory, "
                + "which only constructs this type once that setting is present.");
        }

        var settings = new ElasticsearchClientSettings(new Uri(options.ElasticsearchUri))
            .DefaultIndex(options.ElasticsearchIndexName);

        if (!string.IsNullOrWhiteSpace(options.ElasticsearchApiKey))
        {
            settings = settings.Authentication(new ApiKey(options.ElasticsearchApiKey));
        }

        _client = new ElasticsearchClient(settings);
        _indexName = options.ElasticsearchIndexName;
        _embeddingProvider = embeddingProvider ?? new DeterministicEmbeddingProvider();
    }

    public async ValueTask<IndexQueryResult> QueryAsync(
        SearchIndexFilter filter, IReadOnlyList<string> tokens, int topN, CancellationToken cancellationToken = default)
    {
        var queryText = string.Join(' ', tokens);

        var response = await _client.SearchAsync<ElasticsearchListingDocument>(search => search
            .Indices(_indexName)
            .Size(topN)
            .Query(q => q.Bool(b => b
                .Filter(BuildFilterClauses(filter))
                .Must(m => m.MultiMatch(mm => mm
                    .Query(queryText)
                    .Fields(MatchFields))))), cancellationToken)
            .ConfigureAwait(false);

        return ToResult(response);
    }

    public async ValueTask<IndexQueryResult> VectorQueryAsync(
        SearchIndexFilter filter, IReadOnlyList<double> queryEmbedding, int topN, CancellationToken cancellationToken = default)
    {
        var vector = queryEmbedding.Select(v => (float)v).ToArray();

        var response = await _client.SearchAsync<ElasticsearchListingDocument>(search => search
            .Indices(_indexName)
            .Size(topN)
            .Knn(knn => knn
                .Field(f => f.Embedding)
                .QueryVector(vector)
                .K(topN)
                .NumCandidates(Math.Max(topN * 10, 50))
                .Filter(BuildFilterClauses(filter))), cancellationToken)
            .ConfigureAwait(false);

        return ToResult(response);
    }

    public async ValueTask IndexAsync(ListingDocument document, CancellationToken cancellationToken = default)
    {
        var outcome = await _embeddingProvider
            .EmbedAsync($"{document.Title} {document.Description}", cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Degraded)
        {
            throw new InvalidOperationException(
                $"Embedding provider degraded ({outcome.DegradationKind ?? "unknown"}) while indexing "
                + $"listing '{document.ListingId}'. IngestionConsumer treats this the same as any other "
                + "failed apply — the event_id reservation is released so a corrected replay is not "
                + "mistaken for a duplicate (SPEC §7.2).");
        }

        var embedding = outcome.Vector!.Select(v => (float)v).ToArray();

        var doc = new ElasticsearchListingDocument
        {
            ListingId = document.ListingId,
            Title = document.Title,
            Description = document.Description,
            City = document.City,
            PriceChf = document.PriceChf,
            Rooms = document.Rooms,
            Status = document.Status.ToString().ToUpperInvariant(),
            OwnerId = document.OwnerId,
            ListedAt = document.ListedAt,
            Embedding = embedding,
        };

        await _client.IndexAsync(doc, request => request
            .Index(_indexName)
            .Id(document.ListingId), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(string listingId, CancellationToken cancellationToken = default) =>
        await _client.DeleteAsync<ElasticsearchListingDocument>(listingId, request => request
            .Index(_indexName), cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<IndexHealth> HealthAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.PingAsync(cancellationToken).ConfigureAwait(false);
        return new IndexHealth(response.IsValidResponse, response.IsValidResponse ? null : "elasticsearch ping failed");
    }

    private static Action<QueryDescriptor<ElasticsearchListingDocument>>[] BuildFilterClauses(SearchIndexFilter filter)
    {
        var clauses = new List<Action<QueryDescriptor<ElasticsearchListingDocument>>>
        {
            q => q.Terms(t => t
                .Field(f => f.Status)
                .Terms(new TermsQueryField(filter.AllowedStatuses.Select(s => FieldValue.String(s.ToString().ToUpperInvariant())).ToArray()))),
        };

        if (filter.City is { } city)
        {
            clauses.Add(q => q.Term(t => t.Field(f => f.City).Value(city)));
        }

        if (filter.MinPrice is { } minPrice || filter.MaxPrice is { } maxPrice)
        {
            clauses.Add(q => q.Range(r => r.Number(nr => nr
                .Field(f => f.PriceChf)
                .Gte(filter.MinPrice is { } gte ? (double)gte : null)
                .Lte(filter.MaxPrice is { } lte ? (double)lte : null))));
        }

        if (filter.MinRooms is { } minRooms || filter.MaxRooms is { } maxRooms)
        {
            clauses.Add(q => q.Range(r => r.Number(nr => nr
                .Field(f => f.Rooms)
                .Gte(filter.MinRooms is { } gte ? (double)gte : null)
                .Lte(filter.MaxRooms is { } lte ? (double)lte : null))));
        }

        return [.. clauses];
    }

    private static IndexQueryResult ToResult(SearchResponse<ElasticsearchListingDocument> response)
    {
        if (!response.IsValidResponse)
        {
            return new IndexQueryResult([], Degraded: true, DegradationKind: "shard_unavailable");
        }

        var hits = response.Hits
            .Where(hit => hit.Source is not null && hit.Id is not null)
            .Select(hit => new IndexHit(hit.Id!, hit.Source!.ListingId, hit.Score ?? 0))
            .ToList();

        return new IndexQueryResult(hits);
    }
}
