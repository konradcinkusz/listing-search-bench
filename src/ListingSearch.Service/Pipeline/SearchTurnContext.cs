using System.Diagnostics;
using ListingSearch.Service.Search;
using ListingSearch.Service.Telemetry;

namespace ListingSearch.Service.Pipeline;

public enum RetrievalPathKind
{
    Lexical,
    Vector,
}

public sealed record RetrievedCandidate(
    string ListingId,
    RetrievalPathKind Path,
    double Score,
    decimal PriceChf,
    string? ManipulationSignal);

public sealed record RankedCandidate(
    string ListingId,
    int Rank,
    double CombinedScore,
    double LexicalScore,
    double VectorScore,
    ResultAttribution Attribution,
    decimal Price = 0);

/// <summary>
/// The mutable state one search request carries through the pipeline — the same
/// "context bag" shape <c>AgentTurnContext</c> uses in the worked example this
/// repository mirrors. Immutable inputs are constructor parameters; everything a
/// stage fills in is a settable property, in pipeline order.
/// </summary>
public sealed class SearchTurnContext(SearchRequest request)
{
    private readonly List<DegradationNote> _degradations = [];

    public SearchRequest Request { get; } = request;

    public IReadOnlyList<string> Tokens { get; set; } = [];

    public SearchIndexFilter? Filter { get; set; }

    public IReadOnlyList<RetrievedCandidate> LexicalCandidates { get; set; } = [];

    public IReadOnlyList<RetrievedCandidate> VectorCandidates { get; set; } = [];

    public IReadOnlyList<RankedCandidate> Ranked { get; set; } = [];

    public SearchResponse? Response { get; set; }

    public IReadOnlyList<DegradationNote> Degradations => _degradations;

    public void NoteDegradation(string stage, string kind)
    {
        _degradations.Add(new DegradationNote(stage, kind));

        EmitEvent(
            SearchDiagnostics.Events.DegradationNoted,
            (SearchDiagnostics.Attributes.DegradationStage, stage),
            (SearchDiagnostics.Attributes.DegradationKind, kind));
    }

    public static void EmitEvent(string name, params (string Key, object? Value)[] tags)
    {
        var collection = new ActivityTagsCollection();

        foreach (var (key, value) in tags)
        {
            collection[key] = value;
        }

        Activity.Current?.AddEvent(new ActivityEvent(name, tags: collection));
    }
}
