using ListingSearch.Evals.Reporting;

namespace ListingSearch.Evals;

/// <summary>
/// SPEC §8.5: two kinds of skip, reported separately. Every smoke-subset scenario
/// reports <c>skipped:no-credential</c> on a keyless run — never a silent pass, and
/// never conflated with <c>skipped:unimplemented</c>.
/// </summary>
public sealed class Layer2Tests
{
    [Fact]
    public void Smoke_scenarios_report_skipped_no_credential_without_a_key()
    {
        var report = Layer2Run.Execute();

        if (Environment.GetEnvironmentVariable("LLM_API_KEY") is not null)
        {
            Assert.Skip("LLM_API_KEY is set — this test only asserts the credential-less path.");
            return;
        }

        Assert.False(report.Ran);
        Assert.NotEmpty(report.Scenarios);
        Assert.All(report.Scenarios, scenario => Assert.Equal(ScenarioStatus.SkippedNoCredential, scenario.Status));

        Assert.Skip(
            $"skipped:no-credential — no LLM_API_KEY configured. {report.Scenarios.Count} smoke scenario(s) "
            + "would have been judged. See docs/DEVIATIONS.md D-1.");
    }
}
