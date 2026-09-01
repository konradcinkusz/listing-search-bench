using ListingSearch.Evals.Mutations;
using ListingSearch.Evals.Reporting;

namespace ListingSearch.Evals;

/// <summary>
/// SPEC §8.6 — proving the suite can fail. A green Layer 1 run only proves the
/// scenarios can pass; this proves they can catch a broken pipeline, one deliberate
/// mutation at a time.
/// </summary>
public sealed class MutationTests
{
    public static TheoryData<string> VariantNames =>
        [.. BrokenPipeline.All.Select(variant => variant.Name)];

    [Theory]
    [MemberData(nameof(VariantNames))]
    public void Mutant_is_caught_by_the_constraint_layer(string name)
    {
        var variant = BrokenPipeline.All.Single(v => string.Equals(v.Name, name, StringComparison.Ordinal));
        var scenario = Layer1Run.Corpus.FirstOrDefault(s => string.Equals(s.Id, variant.ScenarioId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Mutation '{name}' targets scenario '{variant.ScenarioId}', which is not in the corpus.");

        // A mutant that "fails" a scenario which does not even pass against the real
        // pipeline proves nothing — this sanity check keeps that from being read as success.
        var sane = Layer1Run.Report[variant.ScenarioId];
        Assert.True(
            sane.Passed,
            $"Sanity check failed: '{variant.ScenarioId}' does not pass against the real pipeline "
            + $"(status={sane.Status}), so running it against a broken one would prove nothing.");

        var mutated = Layer1Run.RunOne(scenario, variant.Break);

        Assert.True(
            !mutated.Passed,
            $"Mutation '{name}' survived: '{variant.ScenarioId}' still passed with it applied. "
            + "A survived mutant is a missing assertion, not a curiosity — see docs/FINDINGS.md.");

        Assert.True(
            mutated.Status is ScenarioStatus.Fail or ScenarioStatus.Error,
            $"Mutation '{name}' did not resolve to a graded failure (status={mutated.Status}).");
    }

    [Fact]
    public void Every_variant_targets_a_different_scenario()
    {
        var targets = BrokenPipeline.All.Select(v => v.ScenarioId).ToList();
        Assert.Equal(targets.Distinct(StringComparer.Ordinal).Count(), targets.Count);
    }

    [Fact]
    public void There_are_exactly_four_variants()
    {
        // SPEC §8.6 names four, one per constraint they target (C-2, C-1, C-6, C-7).
        // A fifth added quietly is drift; fewer is a closed gap nobody recorded.
        Assert.Equal(4, BrokenPipeline.All.Count);
    }
}
