namespace ListingSearch.SearchService.Pipeline;

/// <summary>Ranking weights and pool sizes — tuning, not behaviour; SPEC never cites a specific number here.</summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    public int CandidatePoolSize { get; set; } = 50;

    public double LexicalWeight { get; set; } = 0.55;

    public double VectorWeight { get; set; } = 0.45;

    /// <summary>The fractional score multiplier applied above SearchRequest.SoftMaxPrice (SPEC B-6).</summary>
    public double SoftPricePenalty { get; set; } = 0.5;

    public int MaxStages { get; set; } = 16;
}
