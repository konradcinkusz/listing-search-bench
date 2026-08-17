using System.Diagnostics;
using ListingSearch.Service.Search;
using ListingSearch.Service.Telemetry;

namespace ListingSearch.Service.Pipeline;

public interface ISearchOrchestrator
{
    ValueTask<SearchResponse> RunAsync(SearchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs every registered <see cref="ISearchStage"/>, in DI-registration order — the
/// same discipline <c>AgentOrchestrator</c> uses in the worked example this repository
/// mirrors, for the same reason: the pipeline's order <em>is</em> the specification
/// (SPEC's stage list), and an orchestrator that built its own list independently of
/// the registrations could keep passing after somebody reordered them.
/// </summary>
public sealed class SearchOrchestrator : ISearchOrchestrator
{
    private readonly IReadOnlyList<ISearchStage> _stages;
    private readonly SearchOptions _options;

    public SearchOrchestrator(IEnumerable<ISearchStage> stages, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(options);

        _stages = [.. stages];
        _options = options;
    }

    public async ValueTask<SearchResponse> RunAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = SearchDiagnostics.Source.StartActivity("search_turn", ActivityKind.Server);

        var context = new SearchTurnContext(request);
        var stagesRun = 0;

        foreach (var stage in _stages)
        {
            if (stagesRun >= _options.MaxStages)
            {
                // A closed, fixed-length pipeline should never reach this — it exists
                // so a future stage that adds itself twice by DI-registration mistake
                // fails loudly in a test rather than looping until it times out.
                throw new InvalidOperationException(
                    $"The search pipeline ran {stagesRun} stages without completing, past "
                    + $"SearchOptions.MaxStages ({_options.MaxStages}). This is a registration defect, "
                    + "not a runtime condition any request can trigger honestly.");
            }

            stagesRun++;

            if (await RunStageAsync(stage, context, cancellationToken).ConfigureAwait(false) == StageSignal.Stop)
            {
                break;
            }
        }

        var response = context.Response ?? new SearchResponse([], SearchOutcome.Completed, context.Degradations);

        activity?.SetTag(SearchDiagnostics.Attributes.TurnOutcome, response.Outcome.ToString().ToLowerInvariant());
        activity?.SetTag(
            SearchDiagnostics.Attributes.TerminationReason,
            response.Outcome == SearchOutcome.Degraded
                ? SearchDiagnostics.TerminationReasons.Degraded
                : SearchDiagnostics.TerminationReasons.Resolved);

        return response;
    }

    private static async ValueTask<StageSignal> RunStageAsync(
        ISearchStage stage, SearchTurnContext context, CancellationToken cancellationToken)
    {
        using var activity = SearchDiagnostics.Source.StartActivity($"search_stage {stage.Name}", ActivityKind.Internal);
        activity?.SetTag(SearchDiagnostics.Attributes.StageName, stage.Name);

        if (!stage.AppliesTo(context))
        {
            activity?.SetTag(SearchDiagnostics.Attributes.StageApplied, false);
            return StageSignal.Continue;
        }

        activity?.SetTag(SearchDiagnostics.Attributes.StageApplied, true);
        return await stage.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }
}
