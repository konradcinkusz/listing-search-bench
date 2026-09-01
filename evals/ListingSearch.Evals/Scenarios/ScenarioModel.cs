using System.Globalization;
using YamlDotNet.Serialization;

namespace ListingSearch.Evals.Scenarios;

/// <summary>
/// The YAML shape of one scenario file, deserialized loosely (YamlDotNet,
/// <c>IgnoreUnmatchedProperties</c>). Schema enforcement — required fields, closed
/// enums, cross-field rules — is <c>evals/schema/scenario.schema.json</c>, validated
/// separately by <c>scripts/validate-scenarios.mjs</c> in CI, the same split the
/// worked example this repository mirrors makes: the C# types here re-parse
/// loosely and fail loudly only on what they cannot interpret, rather than
/// reimplementing schema validation.
/// </summary>
public sealed class ScenarioFile
{
    public string Id { get; set; } = "";

    public string Class { get; set; } = "";

    public string Gate { get; set; } = "behaviour";

    public string Title { get; set; } = "";

    public string Why { get; set; } = "";

    public ScenarioOrigin Origin { get; set; } = new();

    public ScenarioFixture Fixture { get; set; } = new();

    public List<ScenarioStep> Steps { get; set; } = [];

    public List<ScenarioAssertion> Expect { get; set; } = [];

    public List<string> Rubrics { get; set; } = [];

    public ScenarioSkip? Skip { get; set; }

    public bool IsConstraint => string.Equals(Gate, "constraint", StringComparison.Ordinal);
}

public sealed class ScenarioOrigin
{
    public string Kind { get; set; } = "designed";

    public string? Date { get; set; }
}

public sealed class ScenarioFixture
{
    public string Base { get; set; } = "";

    /// <summary>Additional listings layered onto the base fixture, same shape as a fixture's own <c>listings:</c> entries.</summary>
    public List<Dictionary<string, object>> Overrides { get; set; } = [];

    /// <summary>Per-operation fault injection: <c>query</c> / <c>vector_query</c> keyed, applied by <c>FaultInjectingSearchIndex</c>.</summary>
    public Dictionary<string, IndexBehaviour> IndexBehaviour { get; set; } = [];
}

public sealed class IndexBehaviour
{
    /// <summary>One of: success, timeout, shard_unavailable, malformed_embedding, empty.</summary>
    public string Outcome { get; set; } = "success";

    /// <summary>If set, the fault applies from this call number onward (1-based); earlier calls succeed.</summary>
    public int? AfterCalls { get; set; }
}

public sealed class ScenarioStep
{
    /// <summary>"search" or "ingest".</summary>
    public string Action { get; set; } = "";

    public ScenarioSearchRequest? Request { get; set; }

    public ScenarioIngestionEvent? Event { get; set; }
}

public sealed class ScenarioSearchRequest
{
    public string Query { get; set; } = "";

    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    public string? City { get; set; }

    public decimal? MinRooms { get; set; }

    public decimal? MaxRooms { get; set; }

    public decimal? SoftMaxPrice { get; set; }

    public string? Sort { get; set; }

    public int? Top { get; set; }
}

public sealed class ScenarioIngestionEvent
{
    public string EventId { get; set; } = "";

    /// <summary>published / price_changed / delisted.</summary>
    public string Type { get; set; } = "";

    public string ListingId { get; set; } = "";

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? City { get; set; }

    public decimal? PriceChf { get; set; }

    public decimal? Rooms { get; set; }

    public string? OwnerId { get; set; }
}

/// <summary>
/// One assertion. Loosely typed on purpose — not every field applies to every
/// <see cref="Assert"/> value, and <c>AssertionEvaluator</c> is the single place that
/// says which fields a given assertion type reads.
/// </summary>
public sealed class ScenarioAssertion
{
    public string Assert { get; set; } = "";

    public int? Step { get; set; }

    public string? Listing { get; set; }

    public string? Than { get; set; }

    public string? Path { get; set; }

    public string? Value { get; set; }

    public int? Times { get; set; }

    public int? AtLeast { get; set; }

    public int? AtMost { get; set; }

    public string? Event { get; set; }

    public string? Operation { get; set; }

    public int? MaxAttempts { get; set; }

    public string? Attribute { get; set; }

    /// <summary>Named EqualsValue in code — "Equals" collides with <see cref="object.Equals(object?)"/> — but "equals" in every scenario YAML file.</summary>
    [YamlMember(Alias = "equals")]
    public string? EqualsValue { get; set; }

    public ScenarioSpanReference? Span { get; set; }

    public string Describe() => Assert switch
    {
        "result_includes" or "result_excludes" =>
            $"{Assert}({Listing}, step={Step?.ToString(CultureInfo.InvariantCulture) ?? "last search"})",
        "candidate_set_includes" or "candidate_set_excludes" => $"{Assert}({Listing}, path={Path ?? "any"})",
        "result_rank" => $"result_rank({Listing}, at_most={AtMost}, equals={Value})",
        "result_ranked_below" => $"result_ranked_below({Listing} < {Than})",
        "result_attribution" => $"result_attribution({Listing} == {Value})",
        "result_count" => $"result_count(step={Step}, equals={Value}, at_least={AtLeast}, at_most={AtMost})",
        "outcome" => $"outcome(step={Step?.ToString(CultureInfo.InvariantCulture) ?? "last search"}) == {Value}",
        "ingestion_outcome" => $"ingestion_outcome(step={Step}) == {Value}",
        "event_emitted" or "event_not_emitted" => $"{Assert}({Event})",
        "call_attempts" => $"call_attempts({Operation}) <= {MaxAttempts}",
        "response_excludes_internal_fields" => "response_excludes_internal_fields()",
        "span_attribute" => $"span_attribute({Attribute}) == {EqualsValue}",
        _ => Assert,
    };
}

public sealed class ScenarioSpanReference
{
    public string? Stage { get; set; }

    public string? Event { get; set; }

    public string? IndexOperation { get; set; }
}

public sealed class ScenarioSkip
{
    public string Reason { get; set; } = "";

    public string? Since { get; set; }
}
