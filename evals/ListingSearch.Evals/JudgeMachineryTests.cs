using ListingSearch.Evals.Execution;
using ListingSearch.Evals.Judging;
using ListingSearch.Evals.Judging.Llm;

namespace ListingSearch.Evals;

/// <summary>
/// Tests the judge's machinery — parsing, configuration, hashing, kappa arithmetic —
/// with no model, no network and no credential. This is what makes it honest to say
/// the judge is "built and tested" while also saying, plainly, that it has never
/// scored a live model (docs/DEVIATIONS.md D-1): everything in this file is testable
/// without one, and nothing in this file claims to test the part that needs one.
/// </summary>
public sealed class JudgeMachineryTests
{
    private static readonly JudgeConfiguration Configuration = JudgeConfiguration.Instance.Value;

    [Fact]
    public void Every_rubric_has_an_anchor_for_every_level_of_its_scale()
    {
        // JudgeConfiguration.Load() already throws if this is false — reaching this
        // line at all is the assertion.
        Assert.NotEmpty(Configuration.RubricNames);
    }

    [Fact]
    public void The_rubrics_and_prompt_hashes_are_stable_and_nonempty()
    {
        Assert.Equal(12, Configuration.RubricsHash.Length);
        Assert.Equal(12, Configuration.PromptHash.Length);
        Assert.Equal(Configuration.RubricsHash, JudgeConfiguration.Instance.Value.RubricsHash);
    }

    [Fact]
    public void The_smoke_subset_names_scenarios_that_exist_in_the_corpus()
    {
        var corpusIds = Reporting.Layer1Run.Corpus.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var entry in Configuration.Smoke)
        {
            Assert.Contains(entry.Id, corpusIds);
        }
    }

    [Fact]
    public void RubricJudge_parses_a_well_formed_response()
    {
        var response = new LlmResponse(
            """{"relevance": {"score": 3, "justification": "lst-1001 leads and matches the query."}}""",
            "test-model-v1");

        var verdict = RubricJudge.Parse(response, ["relevance"], Configuration);

        Assert.Equal("test-model-v1", verdict.Model);
        Assert.Single(verdict.Scores);
        Assert.Equal(3, verdict.Scores[0].Score);
    }

    [Fact]
    public void RubricJudge_tolerates_a_fenced_json_wrapper()
    {
        var response = new LlmResponse(
            "```json\n{\"relevance\": {\"score\": 2, \"justification\": \"Reasonable but not first.\"}}\n```",
            "test-model-v1");

        var verdict = RubricJudge.Parse(response, ["relevance"], Configuration);
        Assert.Equal(2, verdict.Scores[0].Score);
    }

    [Fact]
    public void RubricJudge_rejects_a_score_outside_the_rubrics_scale()
    {
        var response = new LlmResponse("""{"relevance": {"score": 9, "justification": "x"}}""", "test-model-v1");

        Assert.Throws<InvalidOperationException>(() => RubricJudge.Parse(response, ["relevance"], Configuration));
    }

    [Fact]
    public void RubricJudge_rejects_a_decimal_score()
    {
        var response = new LlmResponse("""{"relevance": {"score": 1.5, "justification": "x"}}""", "test-model-v1");

        Assert.Throws<InvalidOperationException>(() => RubricJudge.Parse(response, ["relevance"], Configuration));
    }

    [Fact]
    public void RubricJudge_rejects_a_missing_criterion()
    {
        var response = new LlmResponse("""{"relevance": {"score": 2, "justification": "x"}}""", "test-model-v1");

        Assert.Throws<InvalidOperationException>(
            () => RubricJudge.Parse(response, ["relevance", "attribution-clarity"], Configuration));
    }

    [Fact]
    public void RubricJudge_rejects_an_invented_criterion()
    {
        var response = new LlmResponse(
            """{"relevance": {"score": 2, "justification": "x"}, "made_up": {"score": 1, "justification": "y"}}""",
            "test-model-v1");

        Assert.Throws<InvalidOperationException>(() => RubricJudge.Parse(response, ["relevance"], Configuration));
    }

    [Fact]
    public void RubricJudge_rejects_prose_with_no_json_object()
    {
        var response = new LlmResponse("I think this is a pretty good result, score it highly.", "test-model-v1");

        Assert.Throws<InvalidOperationException>(() => RubricJudge.Parse(response, ["relevance"], Configuration));
    }

    [Fact]
    public void The_same_run_renders_to_the_same_transcript()
    {
        var loaded = Reporting.Layer1Run.Corpus.First(s => s.Id == "hap-001-price-and-city-hard-filter");
        var run = ScenarioRunner.Execute(loaded);

        var first = Judging.TraceNarrative.Render(loaded, run);
        var second = Judging.TraceNarrative.Render(loaded, run);

        Assert.Equal(first, second);
        Assert.Contains("### Execution trace", first);
        Assert.Contains("### What came back", first);
    }

    [Theory]
    [InlineData(new[] { 3, 3, 3, 3 }, new[] { 3, 3, 3, 3 }, null)] // every pair in one category — undefined, not 1.0
    [InlineData(new[] { 3, 2, 1, 0 }, new[] { 3, 2, 1, 0 }, 1.0)] // perfect agreement across categories
    public void CohenKappa_matches_known_cases(int[] judge, int[] human, double? expected)
    {
        var pairs = judge.Zip(human).ToList();
        var kappa = Calibration.CohenKappa(pairs);

        if (expected is null)
        {
            Assert.Null(kappa);
        }
        else
        {
            Assert.NotNull(kappa);
            Assert.Equal(expected.Value, kappa.Value, precision: 6);
        }
    }

    [Fact]
    public void Calibration_does_not_gate_with_zero_labels()
    {
        var report = Calibration.Summarise([], [], Configuration.Calibration);

        Assert.False(report.Gating);
        Assert.NotEmpty(report.Reasons);
    }
}
