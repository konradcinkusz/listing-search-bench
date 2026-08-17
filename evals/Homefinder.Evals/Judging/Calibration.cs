namespace Homefinder.Evals.Judging;

public sealed record HumanLabel(string ScenarioId, string Rubric, int Score, string Labeller, DateOnly Date);

public sealed record CalibrationReport(
    int TotalLabels,
    int DistinctScenarios,
    double? Kappa,
    bool Gating,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Whether judge scores may gate anything — SPEC §5's closing sentence, made
/// checkable. Four conditions, all evaluated here; a fifth (labels recorded under
/// the repository owner's own handle, not an AI rater's) is stated in
/// <c>evals/calibration/README.md</c> rather than enforced here, because "whose
/// handle this is" is not a fact this file can verify from the label alone.
/// </summary>
public static class Calibration
{
    /// <summary>
    /// Unweighted Cohen's kappa. Returns null — not 1.0 — when every pair falls in a
    /// single category: "perfect agreement" there is undefined, not perfect, because
    /// chance alone predicts it as reliably as genuine agreement would.
    /// </summary>
    public static double? CohenKappa(IReadOnlyList<(int Judge, int Human)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count < 2)
        {
            return null;
        }

        var total = (double)pairs.Count;
        var observed = pairs.Count(pair => pair.Judge == pair.Human) / total;

        var categories = pairs.SelectMany(pair => new[] { pair.Judge, pair.Human }).Distinct().ToList();

        var expected = categories.Sum(category =>
            (pairs.Count(pair => pair.Judge == category) / total)
            * (pairs.Count(pair => pair.Human == category) / total));

        return Math.Abs(1 - expected) < 1e-9 ? null : (observed - expected) / (1 - expected);
    }

    public static CalibrationReport Summarise(
        IReadOnlyList<HumanLabel> labels, IReadOnlyList<(int Judge, int Human)> judgeHumanPairs, CalibrationGateConfig gate)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(judgeHumanPairs);
        ArgumentNullException.ThrowIfNull(gate);

        var reasons = new List<string>();
        var distinctScenarios = labels.Select(label => label.ScenarioId).Distinct(StringComparer.Ordinal).Count();
        var kappa = CohenKappa(judgeHumanPairs);

        if (labels.Count < gate.MinimumLabels)
        {
            reasons.Add($"{labels.Count} labels recorded, {gate.MinimumLabels} required.");
        }

        if (distinctScenarios < gate.MinimumScenarios)
        {
            reasons.Add($"{distinctScenarios} distinct scenarios labelled, {gate.MinimumScenarios} required.");
        }

        if (kappa is null)
        {
            reasons.Add("Kappa is undefined — every judge/human pair labelled so far falls in one category.");
        }
        else if (kappa < gate.MinimumKappa)
        {
            reasons.Add($"Kappa is {kappa:F2}, below the {gate.MinimumKappa:F2} threshold.");
        }

        return new CalibrationReport(labels.Count, distinctScenarios, kappa, reasons.Count == 0, reasons);
    }
}
