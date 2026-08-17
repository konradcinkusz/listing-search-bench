using Homefinder.Evals.Reporting;

namespace Homefinder.Evals;

/// <summary>One test case per scenario id — readable failure attribution over one shared corpus run (Reporting.Layer1Run.Report).</summary>
public sealed class Layer1Tests
{
    public static TheoryData<string> ScenarioIds =>
        [.. Layer1Run.Corpus.Select(scenario => scenario.Id)];

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void Scenario_satisfies_its_assertions(string id)
    {
        var result = Layer1Run.Report[id];

        if (result.Status == ScenarioStatus.SkippedUnimplemented)
        {
            Assert.Skip($"{id}: {result.SkipReason}");
            return;
        }

        if (result.Status == ScenarioStatus.Error)
        {
            Assert.Fail($"{id}: the harness itself failed — {result.Error}");
            return;
        }

        var failures = string.Join("\n", result.Failures.Select(f => $"  - {f.Assertion}: {f.Detail}"));
        Assert.True(result.Passed, $"{id} [{(result.IsConstraint ? "constraint" : "behaviour")}] failed:\n{failures}");
    }
}

/// <summary>Whole-corpus gates: plain facts against the one shared report, not per-scenario.</summary>
public sealed class Layer1GateTests
{
    [Fact]
    public void Corpus_is_not_empty()
    {
        Assert.True(Layer1Run.Corpus.Count >= 20, $"Only {Layer1Run.Corpus.Count} scenarios — an empty or tiny corpus passes every gate vacuously.");
    }

    [Fact]
    public void Every_scenario_class_is_represented()
    {
        var classes = Layer1Run.Corpus.Select(s => s.Scenario.Class).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        string[] required = ["happy", "ambiguity", "exclusion", "adversarial", "degradation"];

        foreach (var required1 in required)
        {
            Assert.Contains(required1, classes);
        }
    }

    [Fact]
    public void Constraint_scenarios_pass_at_100_percent()
    {
        var failures = Layer1Run.Report.Scenarios
            .Where(result => result.IsConstraint && result.Status is not ScenarioStatus.SkippedUnimplemented)
            .Where(result => !result.Passed)
            .ToList();

        Assert.True(
            failures.Count == 0,
            "Constraint scenarios hard-block at 100% — no averaging. Failing:\n"
            + string.Join("\n", failures.Select(f => $"  - {f.Id} ({f.Status})")));
    }

    [Fact]
    public void Behaviour_scenarios_do_not_regress_the_baseline()
    {
        var baselinePath = System.IO.Path.Combine(Homefinder.Evals.Scenarios.RepositoryLayout.BaselinesRoot, "layer1.json");
        var baseline = Baseline.Load(baselinePath);

        var regressions = Layer1Run.Report.Scenarios
            .Where(result => !result.IsConstraint && result.Status is not ScenarioStatus.SkippedUnimplemented)
            .Where(result => !result.Passed && string.Equals(baseline.StatusOf(result.Id), ScenarioStatus.Pass, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            regressions.Count == 0,
            "Behaviour scenarios are measured against the recorded baseline — a scenario that was passing "
            + "and is not any more is a regression:\n"
            + string.Join("\n", regressions.Select(f => $"  - {f.Id}")));
    }

    [Fact]
    public void Baseline_scenario_ids_match_the_corpus_exactly()
    {
        var baselinePath = System.IO.Path.Combine(Homefinder.Evals.Scenarios.RepositoryLayout.BaselinesRoot, "layer1.json");
        var baseline = Baseline.Load(baselinePath);

        var corpusIds = Layer1Run.Corpus.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var baselineIds = baseline.Scenarios.Keys.ToHashSet(StringComparer.Ordinal);

        var missing = corpusIds.Except(baselineIds).ToList();
        var stale = baselineIds.Except(corpusIds).ToList();

        Assert.True(
            missing.Count == 0 && stale.Count == 0,
            $"Baseline drift — missing from baseline: [{string.Join(", ", missing)}]; stale in baseline (no longer in corpus): [{string.Join(", ", stale)}]");
    }

    [Fact]
    public void Layer_1_runs_within_its_budget()
    {
        // SPEC §8.1 — 2 minutes for the whole Layer 1 corpus on a PR.
        Assert.True(
            Layer1Run.Report.Elapsed < TimeSpan.FromMinutes(2),
            $"Layer 1 took {Layer1Run.Report.Elapsed.TotalSeconds:F1}s, over the 2-minute PR budget. Pruned, not renamed.");
    }
}
