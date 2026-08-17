using System.Diagnostics;
using ListingSearch.Evals.Scenarios;
using ListingSearch.Evals.World;
using ListingSearch.Service.Extensions;
using ListingSearch.Service.Ingestion;
using ListingSearch.Service.Pipeline;
using ListingSearch.Service.Search;
using ListingSearch.Service.Search.Fixtures;
using ListingSearch.Service.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace ListingSearch.Evals.Execution;

public sealed record StepResult(int Index, string Action, SearchResponse? Search, IngestionOutcome? Ingest);

/// <summary>
/// What one scenario produced: each step's direct result, that step's own trace
/// slice (<see cref="StepTraces"/>, same index as <see cref="Steps"/>), and the whole
/// scenario's merged trace for assertions that are not step-scoped.
/// </summary>
public sealed record ScenarioRun(
    IReadOnlyList<StepResult> Steps,
    IReadOnlyList<TraceRecording> StepTraces,
    TraceRecording FullTrace,
    EvalWorld World);

/// <summary>
/// Runs one scenario end to end and hands back its trace.
///
/// <para>
/// It builds the pipeline <b>through the real composition root</b>
/// (<c>AddSearchPipeline</c>) rather than assembling the stage list itself — the
/// pipeline's order is the specification, and a harness that built its own list would
/// keep passing after somebody reordered the registrations. It substitutes only the
/// index (fixture-seeded, fault-injectable) and the ingestion catalogue, exactly what
/// a scenario is (SPEC §8.3).
/// </para>
/// <para>Nothing survives between scenarios: a fresh service provider, a fresh idempotency store, a fresh catalogue.</para>
/// </summary>
public static class ScenarioRunner
{
    private const string ScopeName = "ListingSearch.Evals.Scope";

    private static readonly ActivitySource Scope = new(ScopeName);

    public static ScenarioRun Execute(LoadedScenario loaded, Action<IServiceCollection>? mutate = null)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        var scenario = loaded.Scenario;
        var world = FixtureComposer.Compose(loaded);

        var captured = new List<Activity>();
        var traces = new HashSet<ActivityTraceId>();

        using var tracer = Sdk.CreateTracerProviderBuilder()
            .AddSource(SearchDiagnostics.ActivitySourceName)
            .AddSource(ScopeName)
            .AddInMemoryExporter(captured)
            .Build()!;

        using var provider = BuildProvider(scenario, world, mutate);

        var orchestrator = provider.GetRequiredService<ISearchOrchestrator>();
        var ingestion = provider.GetRequiredService<IIngestionConsumer>();

        var steps = new List<StepResult>();
        var stepTraces = new List<TraceRecording>();

        for (var index = 0; index < scenario.Steps.Count; index++)
        {
            var step = scenario.Steps[index];

            var scope = Scope.StartActivity("scenario_step")
                ?? throw new InvalidOperationException(
                    "The scenario scope span was not sampled, so the trace could not be attributed "
                    + "to this run. Every assertion below would read an empty trace and pass.");

            var stepTraceId = scope.TraceId;
            traces.Add(stepTraceId);

            steps.Add(step.Action switch
            {
                "search" => new StepResult(index + 1, "search", RunSearch(orchestrator, step, loaded.Id), null),
                "ingest" => new StepResult(index + 1, "ingest", null, RunIngest(ingestion, step, loaded.Id)),
                _ => throw new InvalidOperationException($"Scenario '{loaded.Id}' has an unknown step action '{step.Action}'."),
            });

            // Stopped and flushed per step (not just once at the end) so StepTraces
            // can be sliced by TraceId without one step's spans bleeding into another
            // step's per-step assertion.
            scope.Dispose();
            tracer.ForceFlush();

            var stepActivities = captured.Where(activity =>
                activity.TraceId == stepTraceId
                && !string.Equals(activity.Source.Name, ScopeName, StringComparison.Ordinal));

            stepTraces.Add(TraceRecording.From(stepActivities));
        }

        tracer.ForceFlush();

        var mine = captured.Where(activity =>
            traces.Contains(activity.TraceId)
            && !string.Equals(activity.Source.Name, ScopeName, StringComparison.Ordinal));

        return new ScenarioRun(steps, stepTraces, TraceRecording.From(mine), world);
    }

    private static ServiceProvider BuildProvider(ScenarioFile scenario, EvalWorld world, Action<IServiceCollection>? mutate)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSearchPipeline(new ConfigurationBuilder().Build());

        services.AddSingleton<IListingCatalog>(new InMemoryListingCatalog(world.Listings));
        services.AddSingleton<IEventIdempotencyStore, InMemoryEventIdempotencyStore>();
        services.AddSingleton<IIngestionConsumer, IngestionConsumer>();

        services.AddSingleton<ISearchIndex>(_ =>
        {
            var indexOptions = new SearchIndexOptions();
            ISearchIndex backend = new InMemoryFixtureIndex(world.Listings);

            return SearchIndexFactory.Instrument(
                backend,
                indexOptions.MaxReadAttempts,
                scenario.Fixture.IndexBehaviour.Count == 0
                    ? null
                    : inner => new FaultInjectingSearchIndex(inner, scenario.Fixture.IndexBehaviour));
        });

        // The mutation pass (SPEC §8.6) swaps a registration here to prove the
        // constraint layer can catch a broken pipeline. Nothing else uses it.
        mutate?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static SearchResponse RunSearch(ISearchOrchestrator orchestrator, ScenarioStep step, string scenarioId)
    {
        var request = step.Request
            ?? throw new InvalidOperationException($"Scenario '{scenarioId}' has a 'search' step with no 'request'.");

        var searchRequest = new SearchRequest(
            QueryText: request.Query,
            MinPrice: request.MinPrice,
            MaxPrice: request.MaxPrice,
            City: request.City,
            MinRooms: request.MinRooms,
            MaxRooms: request.MaxRooms,
            SoftMaxPrice: request.SoftMaxPrice,
            Sort: ParseSort(scenarioId, request.Sort),
            Top: request.Top ?? 10);

        return orchestrator.RunAsync(searchRequest).AsTask().GetAwaiter().GetResult();
    }

    private static IngestionOutcome RunIngest(IIngestionConsumer consumer, ScenarioStep step, string scenarioId)
    {
        var evt = step.Event
            ?? throw new InvalidOperationException($"Scenario '{scenarioId}' has an 'ingest' step with no 'event'.");

        var envelope = new IngestionEnvelope(
            evt.EventId,
            ParseEventType(scenarioId, evt.Type),
            new ListingEventPayload(evt.ListingId, evt.Title, evt.Description, evt.City, evt.PriceChf, evt.Rooms, evt.OwnerId),
            DateTimeOffset.UtcNow);

        return consumer.ConsumeAsync(envelope).AsTask().GetAwaiter().GetResult();
    }

    private static SortOrder ParseSort(string scenarioId, string? sort) => sort switch
    {
        null or "relevance" => SortOrder.Relevance,
        "price_ascending" => SortOrder.PriceAscending,
        "price_descending" => SortOrder.PriceDescending,
        _ => throw new InvalidOperationException($"Scenario '{scenarioId}' has an unrecognised sort '{sort}'."),
    };

    private static ListingEventType ParseEventType(string scenarioId, string type) => type switch
    {
        "published" => ListingEventType.Published,
        "price_changed" => ListingEventType.PriceChanged,
        "delisted" => ListingEventType.Delisted,
        _ => throw new InvalidOperationException($"Scenario '{scenarioId}' has an unrecognised event type '{type}'."),
    };
}
