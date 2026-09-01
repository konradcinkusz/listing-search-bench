using System.Diagnostics;
using ListingSearch.Evals.Assertions;
using ListingSearch.Evals.Execution;
using ListingSearch.Evals.Scenarios;
using Microsoft.Extensions.DependencyInjection;

namespace ListingSearch.Evals.Reporting;

/// <summary>
/// Runs the whole corpus exactly once per test process (budget — SPEC §8.1) and
/// caches the report. <see cref="RunOne"/> is exposed separately so the mutation pass
/// (SPEC §8.6) can re-run a single scenario against a broken pipeline without paying
/// for the whole corpus again.
/// </summary>
public static class Layer1Run
{
    private static readonly Lazy<IReadOnlyList<LoadedScenario>> LazyCorpus = new(ScenarioLoader.LoadAll);

    private static readonly Lazy<EvalReport> LazyReport =
        new(RunAll, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<LoadedScenario> Corpus => LazyCorpus.Value;

    public static EvalReport Report => LazyReport.Value;

    public static ScenarioResult RunOne(LoadedScenario loaded, Action<IServiceCollection>? mutate = null)
    {
        var scenario = loaded.Scenario;

        if (scenario.Skip is { } skip)
        {
            return new ScenarioResult(
                loaded.Id, scenario.Class, scenario.IsConstraint, ScenarioStatus.SkippedUnimplemented, [], SkipReason: skip.Reason);
        }

        try
        {
            var run = ScenarioRunner.Execute(loaded, mutate);
            var outcomes = scenario.Expect.Select(assertion => AssertionEvaluator.Evaluate(assertion, run, loaded.Id)).ToList();
            var passed = outcomes.Count > 0 && outcomes.All(o => o.Passed);

            return new ScenarioResult(
                loaded.Id, scenario.Class, scenario.IsConstraint, passed ? ScenarioStatus.Pass : ScenarioStatus.Fail, outcomes);
        }
#pragma warning disable CA1031 // The harness boundary: any exception becomes a reported "error", distinct from an assertion "fail" — never propagated to crash the whole corpus run over one broken scenario.
        catch (Exception ex)
        {
            return new ScenarioResult(
                loaded.Id, scenario.Class, scenario.IsConstraint, ScenarioStatus.Error, [], Error: ex.Message);
        }
#pragma warning restore CA1031
    }

    private static EvalReport RunAll()
    {
        var stopwatch = Stopwatch.StartNew();
        var results = Corpus.Select(loaded => RunOne(loaded)).ToList();
        stopwatch.Stop();

        return new EvalReport(results, stopwatch.Elapsed);
    }
}
