using System.Text.Json;
using System.Text.Json.Serialization;
using ListingSearch.Evals.Judging.Llm;

namespace ListingSearch.Evals.Judging;

public sealed record RubricScore(string Name, int Score, string Justification);

public sealed record JudgeVerdict(IReadOnlyList<RubricScore> Scores, string Model);

public interface IRubricJudge
{
    ValueTask<JudgeVerdict> ScoreAsync(string prompt, IReadOnlyList<string> rubrics, CancellationToken cancellationToken = default);
}

/// <summary>
/// Calls the model and parses its answer against the pinned rubric scale — strictly.
/// Every rejection below is a distinct, specific exception, deliberately: a caller
/// (<c>Layer2Run</c>) turns each into <c>ScenarioStatus.Error</c> rather than
/// averaging a malformed score into a mean, per the same "no vacuous pass" discipline
/// Layer 1's assertions follow.
/// </summary>
public sealed class RubricJudge(ILlmProvider provider, JudgeConfiguration configuration) : IRubricJudge
{
    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask<JudgeVerdict> ScoreAsync(
        string prompt, IReadOnlyList<string> rubrics, CancellationToken cancellationToken = default)
    {
        var response = await provider.CompleteAsync(new LlmRequest(prompt), cancellationToken).ConfigureAwait(false);
        return Parse(response, rubrics, configuration);
    }

    /// <summary>Pure and static so it is testable with no network and no model — <c>JudgeMachineryTests</c> exercises this directly.</summary>
    public static JudgeVerdict Parse(LlmResponse response, IReadOnlyList<string> expected, JudgeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(configuration);

        var json = ExtractObject(response.Text);

        Dictionary<string, RawEntry>? raw;

        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, RawEntry>>(json, ParseOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The judge's response was not valid JSON: {ex.Message}", ex);
        }

        if (raw is null)
        {
            throw new InvalidOperationException("The judge's response deserialized to nothing.");
        }

        var byNameCaseInsensitive = new Dictionary<string, RawEntry>(raw, StringComparer.OrdinalIgnoreCase);

        var extra = byNameCaseInsensitive.Keys.Except(expected, StringComparer.OrdinalIgnoreCase).ToList();

        if (extra.Count > 0)
        {
            throw new InvalidOperationException(
                $"The judge scored criteria this scenario did not ask for: {string.Join(", ", extra)}. "
                + "A judge inventing criteria is not a pass on the ones it was asked for.");
        }

        var scores = new List<RubricScore>(expected.Count);

        foreach (var name in expected)
        {
            if (!byNameCaseInsensitive.TryGetValue(name, out var entry))
            {
                throw new InvalidOperationException(
                    $"The judge's response is missing rubric '{name}'. A missing criterion is not a "
                    + "zero and not a pass — it is a malformed response.");
            }

            var rubric = configuration[name];

            if (entry.Score < 0 || entry.Score > rubric.Scale)
            {
                throw new InvalidOperationException(
                    $"Rubric '{name}' scored {entry.Score}, outside its 0–{rubric.Scale} scale.");
            }

            if (string.IsNullOrWhiteSpace(entry.Justification))
            {
                throw new InvalidOperationException($"Rubric '{name}' has no justification.");
            }

            scores.Add(new RubricScore(name, entry.Score, entry.Justification));
        }

        return new JudgeVerdict(scores, response.Model);
    }

    /// <summary>Tolerates a fenced ```json wrapper, never prose instead of JSON.</summary>
    private static string ExtractObject(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');

        if (start < 0 || end < start)
        {
            throw new InvalidOperationException(
                "The judge's response contains no JSON object. Expected a single {...} object per "
                + "judge-prompt.md's rule 6, with no prose before or after it.");
        }

        return text[start..(end + 1)];
    }

    private sealed record RawEntry(
        [property: JsonPropertyName("score")] int Score,
        [property: JsonPropertyName("justification")] string Justification);
}
