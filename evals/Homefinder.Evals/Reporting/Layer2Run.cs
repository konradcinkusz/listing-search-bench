using Homefinder.Evals.Judging;
using Homefinder.Evals.Judging.Llm;

namespace Homefinder.Evals.Reporting;

public sealed record Layer2ScenarioResult(string Id, string Status, IReadOnlyList<RubricScore>? Scores = null, string? Error = null);

public sealed record Layer2Report(bool Ran, string RubricsHash, string PromptHash, IReadOnlyList<Layer2ScenarioResult> Scenarios);

/// <summary>
/// The credential gate. If <see cref="BuildJudge"/> returns null, every smoke
/// scenario is stamped <c>skipped:no-credential</c> without an <see cref="IRubricJudge"/>
/// or an <see cref="ILlmProvider"/> ever being constructed — the same shape
/// <c>Layer2Run.JudgeOptions()</c> uses in the worked example this repository
/// mirrors. No <see cref="ILlmProvider"/> implementation ships in this repository
/// (docs/DEVIATIONS.md D-1), so today this always returns null; the environment
/// variable names below are what a keyed nightly run would set to close that gap.
/// </summary>
public static class Layer2Run
{
    public static IRubricJudge? BuildJudge()
    {
        var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        var judgeModel = Environment.GetEnvironmentVariable("LLM_JUDGE_MODEL");
        var endpoint = Environment.GetEnvironmentVariable("LLM_ENDPOINT");

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(judgeModel) || string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        // Deliberately unreachable today: closing D-1 means writing a real
        // ILlmProvider implementation here, not flipping a switch that was already
        // wired. A repository that reached this line without one would be lying
        // about being configured.
        throw new NotSupportedException(
            "LLM_API_KEY, LLM_JUDGE_MODEL and LLM_ENDPOINT are set, but no ILlmProvider implementation "
            + "is wired in this repository yet (docs/DEVIATIONS.md D-1). Writing one, and constructing "
            + "it here, is what closes that deviation.");
    }

    public static Layer2Report Execute()
    {
        var configuration = JudgeConfiguration.Instance.Value;
        var judge = BuildJudge();

        if (judge is null)
        {
            var skipped = configuration.Smoke
                .Select(entry => new Layer2ScenarioResult(entry.Id, ScenarioStatus.SkippedNoCredential))
                .ToList();

            return new Layer2Report(Ran: false, configuration.RubricsHash, configuration.PromptHash, skipped);
        }

        throw new NotSupportedException("Unreachable while BuildJudge() never returns a non-null judge.");
    }
}
