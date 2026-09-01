namespace ListingSearch.Evals.Judging.Llm;

public sealed record LlmRequest(string Prompt);

/// <param name="Text">The raw completion text — RubricJudge.Parse extracts the JSON object from it.</param>
/// <param name="Model">Whatever the provider actually reports it served the request with — read back, never assumed (ADR-0004).</param>
public sealed record LlmResponse(string Text, string Model);

/// <summary>
/// The one seam a real model call would cross. No implementation of this interface
/// ships in this repository — docs/DEVIATIONS.md D-1 states plainly that closing it
/// is exactly the work a keyed run would do, not work this repository has done and
/// is hiding.
/// </summary>
public interface ILlmProvider
{
    ValueTask<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
