using ListingSearch.Evals.Assertions;

namespace ListingSearch.Evals.Reporting;

public static class ScenarioStatus
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string Error = "error";
    public const string SkippedUnimplemented = "skipped:unimplemented";
    public const string SkippedNoCredential = "skipped:no-credential";
}

public sealed record ScenarioResult(
    string Id,
    string Class,
    bool IsConstraint,
    string Status,
    IReadOnlyList<AssertionOutcome> Assertions,
    string? SkipReason = null,
    string? Error = null)
{
    public bool Passed => string.Equals(Status, ScenarioStatus.Pass, StringComparison.Ordinal);

    public IReadOnlyList<AssertionOutcome> Failures => [.. Assertions.Where(a => !a.Passed)];
}

public sealed record EvalReport(IReadOnlyList<ScenarioResult> Scenarios, TimeSpan Elapsed)
{
    public ScenarioResult this[string id] =>
        Scenarios.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"No scenario '{id}' in this report.");

    public string Summarise()
    {
        var byStatus = Scenarios.GroupBy(s => s.Status).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var counts = string.Join(", ", byStatus.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

        return $"Layer 1: {Scenarios.Count} scenarios in {Elapsed.TotalSeconds:F1}s — {counts}";
    }
}
