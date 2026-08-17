namespace ListingSearch.Service.Search.Elasticsearch;

/// <summary>The document shape stored in the real Elasticsearch index — dev-only, ADR-0002.</summary>
public sealed class ElasticsearchListingDocument
{
    public string ListingId { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string City { get; set; } = "";

    public decimal PriceChf { get; set; }

    public decimal Rooms { get; set; }

    public string Status { get; set; } = "";

    public string OwnerId { get; set; } = "";

    public DateTimeOffset ListedAt { get; set; }

    public float[] Embedding { get; set; } = [];
}
