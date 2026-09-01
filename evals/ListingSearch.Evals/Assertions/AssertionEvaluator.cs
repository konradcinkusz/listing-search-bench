using System.Globalization;
using System.Text.RegularExpressions;
using ListingSearch.Evals.Execution;
using ListingSearch.Evals.Scenarios;
using ListingSearch.SearchService.Ingestion;
using ListingSearch.SearchService.Search;
using ListingSearch.SearchService.Telemetry;

namespace ListingSearch.Evals.Assertions;

/// <param name="Assertion">The assertion, in its readable form.</param>
/// <param name="Passed">Whether the run satisfied it.</param>
/// <param name="Detail">What was found instead. Present on failure, and worth reading.</param>
public sealed record AssertionOutcome(string Assertion, bool Passed, string? Detail);

/// <summary>
/// Layer 1, evaluated. Fifteen assertion types, each a deterministic property of the
/// trace or of a step's own returned value — never of listing titles or descriptions,
/// which is the search-domain restatement of ADR-0003 (assertions never match prose):
/// a description reads naturally, a response is graded on structure.
///
/// <para>
/// Three disciplines run through all of them, the same three the worked example this
/// repository mirrors states once rather than fifteen times:
/// </para>
/// <list type="number">
///   <item><b>No assertion passes vacuously.</b> An operation never called, a listing
///     never present — these fail and say so, they are not evidence of restraint.</item>
///   <item><b>An unrecognised assertion is an error, not a pass.</b> The schema and
///     this switch must agree.</item>
///   <item><b>Nothing here reads a listing's title or description.</b> Every check
///     reads a structured field — a listing id, a rank, an event name, a tag — never
///     free text, which is also the discipline that makes C-7's own defence testable
///     without the test itself becoming a second place ranking logic could leak in.</item>
/// </list>
/// </summary>
public static partial class AssertionEvaluator
{
    public static AssertionOutcome Evaluate(ScenarioAssertion assertion, ScenarioRun run, string scenarioId)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        ArgumentNullException.ThrowIfNull(run);

        return assertion.Assert switch
        {
            "result_includes" => ResultIncludes(assertion, run, scenarioId, expected: true),
            "result_excludes" => ResultIncludes(assertion, run, scenarioId, expected: false),
            "candidate_set_includes" => CandidateSetIncludes(assertion, run, expected: true),
            "candidate_set_excludes" => CandidateSetIncludes(assertion, run, expected: false),
            "result_rank" => ResultRank(assertion, run, scenarioId),
            "result_ranked_below" => ResultRankedBelow(assertion, run, scenarioId),
            "result_attribution" => ResultAttributionAssertion(assertion, run, scenarioId),
            "result_count" => ResultCount(assertion, run, scenarioId),
            "outcome" => Outcome(assertion, run, scenarioId),
            "ingestion_outcome" => IngestionOutcomeAssertion(assertion, run, scenarioId),
            "event_emitted" => EventEmitted(assertion, run, expected: true),
            "event_not_emitted" => EventEmitted(assertion, run, expected: false),
            "call_attempts" => CallAttempts(assertion, run),
            "response_excludes_internal_fields" => ResponseExcludesInternalFields(assertion, run),
            "span_attribute" => SpanAttribute(assertion, run),

            _ => throw new ArgumentOutOfRangeException(
                nameof(assertion),
                assertion.Assert,
                "Unknown assertion type. evals/schema/scenario.schema.json and AssertionEvaluator must "
                + "agree; a type the harness does not understand must never be graded as a pass."),
        };
    }

    private static AssertionOutcome ResultIncludes(ScenarioAssertion assertion, ScenarioRun run, string scenarioId, bool expected)
    {
        var step = ResolveSearchStep(assertion, run, scenarioId);
        var ids = step.Search!.Results.Select(r => r.ListingId).ToList();
        var present = ids.Contains(assertion.Listing, StringComparer.Ordinal);

        return Result(assertion, present == expected, $"step {step.Index} results: [{string.Join(", ", ids)}]");
    }

    private static AssertionOutcome CandidateSetIncludes(ScenarioAssertion assertion, ScenarioRun run, bool expected)
    {
        var trace = ResolveTrace(assertion, run);
        var operation = ResolvePathOperation(assertion.Path);

        var calls = operation is null
            ? trace.IndexCalls
            : [.. trace.IndexCalls.Where(call => string.Equals(call.Operation, operation, StringComparison.Ordinal))];

        var candidates = calls.SelectMany(call => call.CandidateListingIds).ToHashSet(StringComparer.Ordinal);
        var present = candidates.Contains(assertion.Listing!);

        return Result(
            assertion,
            present == expected,
            $"candidates ({(operation ?? "any")}): [{string.Join(", ", candidates.Order(StringComparer.Ordinal))}]");
    }

    private static AssertionOutcome ResultRank(ScenarioAssertion assertion, ScenarioRun run, string scenarioId)
    {
        var step = ResolveSearchStep(assertion, run, scenarioId);
        var item = step.Search!.Results.FirstOrDefault(r => string.Equals(r.ListingId, assertion.Listing, StringComparison.Ordinal));

        if (item is null)
        {
            return Result(assertion, false, $"'{assertion.Listing}' is not present in step {step.Index}'s results");
        }

        if (assertion.Value is { } exactText)
        {
            var exact = int.Parse(exactText, CultureInfo.InvariantCulture);
            return Result(assertion, item.Rank == exact, $"actual rank: {item.Rank}");
        }

        if (assertion.AtMost is { } atMost)
        {
            return Result(assertion, item.Rank <= atMost, $"actual rank: {item.Rank}");
        }

        throw new InvalidOperationException(
            $"Scenario '{scenarioId}' has a result_rank assertion with neither 'value' nor 'at_most'.");
    }

    private static AssertionOutcome ResultRankedBelow(ScenarioAssertion assertion, ScenarioRun run, string scenarioId)
    {
        var step = ResolveSearchStep(assertion, run, scenarioId);
        var below = step.Search!.Results.FirstOrDefault(r => string.Equals(r.ListingId, assertion.Listing, StringComparison.Ordinal));
        var above = step.Search!.Results.FirstOrDefault(r => string.Equals(r.ListingId, assertion.Than, StringComparison.Ordinal));

        if (above is null)
        {
            return Result(assertion, false, $"'{assertion.Than}' is not present in step {step.Index}'s results, so nothing to rank below");
        }

        if (below is null)
        {
            // Absent entirely is at least as good as "ranked below" — there is
            // nothing to point at as evidence it out-ranked anything.
            return Result(assertion, true, null);
        }

        return Result(
            assertion,
            below.Rank > above.Rank,
            $"'{assertion.Listing}' rank={below.Rank}, '{assertion.Than}' rank={above.Rank}");
    }

    private static AssertionOutcome ResultAttributionAssertion(ScenarioAssertion assertion, ScenarioRun run, string scenarioId)
    {
        var step = ResolveSearchStep(assertion, run, scenarioId);
        var item = step.Search!.Results.FirstOrDefault(r => string.Equals(r.ListingId, assertion.Listing, StringComparison.Ordinal));

        if (item is null)
        {
            return Result(assertion, false, $"'{assertion.Listing}' is not present in step {step.Index}'s results");
        }

        var expected = ParseAttribution(scenarioId, assertion.Value);
        return Result(assertion, item.Source == expected, $"actual source: {item.Source.ToString().ToLowerInvariant()}");
    }

    private static AssertionOutcome ResultCount(ScenarioAssertion assertion, ScenarioRun run, string scenarioId)
    {
        var step = ResolveSearchStep(assertion, run, scenarioId);
        var count = step.Search!.Results.Count;

        if (assertion.Value is { } exactText)
        {
            var exact = int.Parse(exactText, CultureInfo.InvariantCulture);
            return Result(assertion, count == exact, $"actual count: {count}");
        }

        var okLower = assertion.AtLeast is not { } atLeast || count >= atLeast;
        var okUpper = assertion.AtMost is not { } atMost || count <= atMost;

        return Result(assertion, okLower && okUpper, $"actual count: {count}");
    }

    private static AssertionOutcome Outcome(ScenarioAssertion assertion, ScenarioRun run, string scenarioId)
    {
        var step = ResolveSearchStep(assertion, run, scenarioId);
        var actual = step.Search!.Outcome.ToString().ToLowerInvariant();

        return Result(assertion, string.Equals(actual, assertion.Value, StringComparison.Ordinal), $"actual outcome: {actual}");
    }

    private static AssertionOutcome IngestionOutcomeAssertion(ScenarioAssertion assertion, ScenarioRun run, string scenarioId)
    {
        if (assertion.Step is not { } stepNumber)
        {
            throw new InvalidOperationException($"Scenario '{scenarioId}' has an ingestion_outcome assertion with no 'step'.");
        }

        if (stepNumber < 1 || stepNumber > run.Steps.Count)
        {
            throw new InvalidOperationException($"Scenario '{scenarioId}' has an ingestion_outcome assertion whose step {stepNumber} is out of range.");
        }

        var step = run.Steps[stepNumber - 1];

        if (step.Action != "ingest" || step.Ingest is not { } ingestOutcome)
        {
            throw new InvalidOperationException($"Scenario '{scenarioId}' has an ingestion_outcome assertion on step {stepNumber}, which is not an 'ingest' step.");
        }

        var actual = ingestOutcome switch
        {
            IngestionOutcome.Applied => "applied",
            IngestionOutcome.DuplicateIgnored => "duplicate_ignored",
            IngestionOutcome.Failed => "failed",
            IngestionOutcome.Deferred => "deferred",
            IngestionOutcome.DeadLettered => "dead_lettered",
            _ => throw new InvalidOperationException($"Unhandled IngestionOutcome '{ingestOutcome}'."),
        };

        return Result(assertion, string.Equals(actual, assertion.Value, StringComparison.Ordinal), $"actual: {actual}");
    }

    private static AssertionOutcome EventEmitted(ScenarioAssertion assertion, ScenarioRun run, bool expected)
    {
        var trace = ResolveTrace(assertion, run);
        var count = trace.Events.Count(e => string.Equals(e.Name, assertion.Event, StringComparison.Ordinal));

        if (!expected)
        {
            return Result(assertion, count == 0, count == 0 ? null : $"emitted {count} time(s)");
        }

        if (assertion.Times is { } exact)
        {
            return Result(assertion, count == exact, $"emitted {count} time(s)");
        }

        var minimum = assertion.AtLeast ?? 1;
        return Result(assertion, count >= minimum, $"emitted {count} time(s)");
    }

    private static AssertionOutcome CallAttempts(ScenarioAssertion assertion, ScenarioRun run)
    {
        var trace = ResolveTrace(assertion, run);
        var calls = trace.IndexCalls.Where(call => string.Equals(call.Operation, assertion.Operation, StringComparison.Ordinal)).ToList();

        if (calls.Count == 0)
        {
            return Result(assertion, false, $"operation '{assertion.Operation}' was never called, so the attempt bound proves nothing");
        }

        var worst = calls.Max(call => call.Attempts);
        return Result(assertion, worst <= assertion.MaxAttempts, $"worst call made {worst} attempt(s)");
    }

    private static AssertionOutcome ResponseExcludesInternalFields(ScenarioAssertion assertion, ScenarioRun run)
    {
        var steps = assertion.Step is { } stepNumber
            ? [run.Steps[stepNumber - 1]]
            : run.Steps.Where(step => step.Action == "search").ToList();

        var leaks = new List<string>();

        foreach (var step in steps)
        {
            if (step.Search is null)
            {
                continue;
            }

            foreach (var result in step.Search.Results)
            {
                if (!PublicListingId().IsMatch(result.ListingId))
                {
                    leaks.Add($"step {step.Index}: '{result.ListingId}'");
                }
            }
        }

        return Result(assertion, leaks.Count == 0, leaks.Count == 0 ? null : string.Join("; ", leaks));
    }

    private static AssertionOutcome SpanAttribute(ScenarioAssertion assertion, ScenarioRun run)
    {
        var trace = ResolveTrace(assertion, run);
        var found = new List<object?>();

        if (assertion.Span?.Event is { } eventName)
        {
            found.AddRange(trace.Events
                .Where(e => string.Equals(e.Name, eventName, StringComparison.Ordinal))
                .Select(e => e.Tags.GetValueOrDefault(assertion.Attribute!)));
        }
        else if (assertion.Span?.Stage is { } stageName)
        {
            found.AddRange(trace.Stages
                .Where(s => string.Equals(s.Name, stageName, StringComparison.Ordinal))
                .Select(s => s.Tags.GetValueOrDefault(assertion.Attribute!)));
        }
        else if (assertion.Span?.IndexOperation is { } operation)
        {
            found.AddRange(trace.IndexCalls
                .Where(c => string.Equals(c.Operation, operation, StringComparison.Ordinal))
                .Select(c => c.Tags.GetValueOrDefault(assertion.Attribute!)));
        }
        else
        {
            found.AddRange(trace.Events.Select(e => e.Tags.GetValueOrDefault(assertion.Attribute!)));
            found.AddRange(trace.Stages.Select(s => s.Tags.GetValueOrDefault(assertion.Attribute!)));
            found.AddRange(trace.IndexCalls.Select(c => c.Tags.GetValueOrDefault(assertion.Attribute!)));
        }

        var present = found.Where(value => value is not null).Select(Normalise).ToList();

        if (present.Count == 0)
        {
            return Result(assertion, false, $"no span or event carried '{assertion.Attribute}'");
        }

        return Result(
            assertion,
            present.Contains(assertion.EqualsValue, StringComparer.Ordinal),
            $"found: [{string.Join(", ", present)}]");
    }

    private static StepResult ResolveSearchStep(ScenarioAssertion assertion, ScenarioRun run, string scenarioId)
    {
        if (assertion.Step is { } stepNumber)
        {
            if (stepNumber < 1 || stepNumber > run.Steps.Count)
            {
                throw new InvalidOperationException($"Scenario '{scenarioId}' asserts against step {stepNumber}, which is out of range.");
            }

            var step = run.Steps[stepNumber - 1];

            return step.Action == "search"
                ? step
                : throw new InvalidOperationException($"Scenario '{scenarioId}' step {stepNumber} is a '{step.Action}' step, not 'search'.");
        }

        return run.Steps.LastOrDefault(step => step.Action == "search")
            ?? throw new InvalidOperationException($"Scenario '{scenarioId}' has no 'search' step for an assertion that needs one.");
    }

    private static TraceRecording ResolveTrace(ScenarioAssertion assertion, ScenarioRun run) =>
        assertion.Step is { } stepNumber ? run.StepTraces[stepNumber - 1] : run.FullTrace;

    private static string? ResolvePathOperation(string? path) => path switch
    {
        null or "any" => null,
        "lexical" => SearchIndexOperationCatalog.Query,
        "vector" => SearchIndexOperationCatalog.VectorQuery,
        _ => throw new InvalidOperationException($"Unrecognised candidate_set path '{path}'. Valid values: lexical, vector, any."),
    };

    private static ResultAttribution ParseAttribution(string scenarioId, string? value) => value switch
    {
        "lexical" => ResultAttribution.Lexical,
        "vector" => ResultAttribution.Vector,
        "both" => ResultAttribution.Both,
        _ => throw new InvalidOperationException($"Scenario '{scenarioId}' has a result_attribution assertion with unrecognised value '{value}'."),
    };

    /// <summary>
    /// Span tags are typed — an int stays an int, a bool stays a bool — while a
    /// scenario's <c>equals</c> arrives from YAML as text. Normalising here keeps the
    /// comparison honest without making every scenario quote its numbers.
    /// </summary>
    private static string? Normalise(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static AssertionOutcome Result(ScenarioAssertion assertion, bool passed, string? detail) =>
        new(assertion.Describe(), passed, passed ? null : detail);

    [GeneratedRegex(@"^lst-[0-9]{3,5}$", RegexOptions.None)]
    private static partial Regex PublicListingId();
}
