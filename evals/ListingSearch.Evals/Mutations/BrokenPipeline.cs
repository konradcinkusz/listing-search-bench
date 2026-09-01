using ListingSearch.SearchService.Ingestion;
using ListingSearch.SearchService.Pipeline;
using ListingSearch.SearchService.Pipeline.Stages;
using Microsoft.Extensions.DependencyInjection;

namespace ListingSearch.Evals.Mutations;

/// <param name="Name">Stable across runs — cited in FINDINGS.md and in test output.</param>
/// <param name="ScenarioId">The one scenario this variant must fail. Chosen for the narrowest, most direct proof — not "some scenario or other happens to notice".</param>
/// <param name="Break">Applied to the DI container the same way SPEC's mutation pass swaps a registration, not a flag.</param>
public sealed record MutationVariant(string Name, string ScenarioId, Action<IServiceCollection> Break);

/// <summary>
/// The mutation pass, SPEC §8.6. Four deliberately broken variants, each swapping
/// exactly one registration — a pipeline stage or the ingestion consumer — with no
/// flag and no log announcing which one ran. <c>MutationTests</c> asserts the
/// constraint layer catches every one; <c>docs/FINDINGS.md</c> records the run.
/// </summary>
public static class BrokenPipeline
{
    public static IReadOnlyList<MutationVariant> All { get; } =
    [
        new(
            "disable-hard-price-filter",
            "exc-005-high-score-never-buys-a-way-past-a-price-ceiling",
            services => ReplaceStage<FilterResolverStage, DisablesHardPriceFilterStage>(services)),

        new(
            "skip-delisted-check-on-vector-path",
            "exc-001-delisted-never-appears-despite-top-lexical-score",
            services => ReplaceStage<VectorRetrieverStage, SkipsDelistedCheckOnVectorPathStage>(services)),

        new(
            "apply-event-twice-on-retry",
            "adv-002-replayed-publish-event-does-not-resurrect-a-delisting",
            services => ReplaceIngestionConsumer<AppliesEventRegardlessOfIdempotencyConsumer>(services)),

        new(
            "rerank-boosts-flagged-text",
            "adv-001-ranking-manipulation-in-listing-text-is-ignored",
            services => ReplaceStage<HybridRankerStage, BoostsFlaggedTextStage>(services)),
    ];

    private static void ReplaceStage<TOriginal, TMutant>(IServiceCollection services)
        where TOriginal : class, ISearchStage
        where TMutant : class, ISearchStage
    {
        for (var index = 0; index < services.Count; index++)
        {
            if (services[index].ServiceType == typeof(ISearchStage) && services[index].ImplementationType == typeof(TOriginal))
            {
                services[index] = ServiceDescriptor.Singleton<ISearchStage, TMutant>();
                return;
            }
        }

        throw new InvalidOperationException(
            $"No ISearchStage registered as {typeof(TOriginal).Name} — the mutation has nothing to replace. "
            + "Either the composition root changed or this mutation is stale.");
    }

    private static void ReplaceIngestionConsumer<TMutant>(IServiceCollection services)
        where TMutant : class, IIngestionConsumer
    {
        for (var index = 0; index < services.Count; index++)
        {
            if (services[index].ServiceType == typeof(IIngestionConsumer))
            {
                services[index] = ServiceDescriptor.Singleton<IIngestionConsumer, TMutant>();
                return;
            }
        }

        throw new InvalidOperationException("No IIngestionConsumer registered — the mutation has nothing to replace.");
    }
}
