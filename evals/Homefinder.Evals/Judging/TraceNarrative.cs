using System.Globalization;
using System.Text;
using Homefinder.Evals.Execution;
using Homefinder.Evals.Scenarios;

namespace Homefinder.Evals.Judging;

/// <summary>
/// Renders a captured run into the text a judge reads — a transcript and a top-k,
/// never raw JSON (the same shape <c>TraceNarrative</c> renders in the worked example
/// this repository mirrors). Pure and deterministic: the same run renders to the
/// same bytes every time, asserted by <c>JudgeMachineryTests</c>.
/// </summary>
public static class TraceNarrative
{
    public static string Render(LoadedScenario loaded, ScenarioRun run)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(run);

        var builder = new StringBuilder();

        builder.AppendLine("### Setting");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Fixture: {run.World.FixtureName}, {run.World.Listings.Count} listings.");
        builder.AppendLine();

        builder.AppendLine("### What was asked");

        foreach (var step in loaded.Scenario.Steps)
        {
            builder.AppendLine(step.Action == "search"
                ? "[search] " + RenderRequest(step)
                : "[ingest] " + RenderEvent(step));
        }

        builder.AppendLine();
        builder.AppendLine("### Execution trace");
        RenderTrace(builder, run.FullTrace);

        builder.AppendLine();
        builder.AppendLine("### What came back");

        foreach (var step in run.Steps)
        {
            RenderStepResult(builder, step);
        }

        return builder.ToString();
    }

    private static string RenderRequest(ScenarioStep step)
    {
        var request = step.Request!;
        var parts = new List<string> { $"query=\"{request.Query}\"" };

        if (request.City is { } city)
        {
            parts.Add($"city={city}");
        }

        if (request.MaxPrice is { } maxPrice)
        {
            parts.Add($"max_price={maxPrice.ToString(CultureInfo.InvariantCulture)}");
        }

        if (request.SoftMaxPrice is { } softMaxPrice)
        {
            parts.Add($"soft_max_price={softMaxPrice.ToString(CultureInfo.InvariantCulture)}");
        }

        return string.Join(", ", parts);
    }

    private static string RenderEvent(ScenarioStep step)
    {
        var evt = step.Event!;
        return $"{evt.Type} listing={evt.ListingId} event_id={evt.EventId}";
    }

    private static void RenderTrace(StringBuilder builder, TraceRecording trace)
    {
        foreach (var call in trace.IndexCalls)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- index `{call.Operation}` ({call.Kind}) → {call.CandidateListingIds.Count} candidate(s), {call.Attempts} attempt(s){(call.Degraded ? $", degraded: {call.DegradationKind}" : "")}");
        }

        foreach (var evt in trace.Events)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- event `{evt.Name}`");
        }
    }

    private static void RenderStepResult(StringBuilder builder, StepResult step)
    {
        if (step.Search is { } response)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Step {step.Index} (search) — outcome: {response.Outcome.ToString().ToLowerInvariant()}");

            foreach (var result in response.Results.Take(5))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"> {result.Rank}. {result.ListingId} — source: {result.Source.ToString().ToLowerInvariant()}, score: {result.RankingScore.ToString("F4", CultureInfo.InvariantCulture)}");
            }

            if (response.Results.Count == 0)
            {
                builder.AppendLine("> (no results)");
            }
        }
        else if (step.Ingest is { } outcome)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Step {step.Index} (ingest) — outcome: {outcome.ToString().ToLowerInvariant()}");
        }
    }
}
