using ListingSearch.Service.Ingestion;
using ListingSearch.Service.Pipeline;
using ListingSearch.Service.Pipeline.Stages;
using ListingSearch.Service.Search;
using ListingSearch.Service.Search.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ListingSearch.Service.Extensions;

/// <summary>
/// The composition root, split into three registration methods the same way the
/// worked example this repository mirrors splits <c>AddWorkforceTools</c> from
/// <c>AddAbsenceConciergeAgent</c>: the backend (<see cref="AddSearchIndex"/>) is
/// registered separately from the pipeline (<see cref="AddSearchPipeline"/>) so the
/// eval harness can call the pipeline registration — getting the <em>real</em>,
/// registration-ordered stage list — while constructing its own fixture-seeded index
/// and catalogue instead of reading one off disk.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSearchIndex(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SearchIndexOptions>(configuration.GetSection(SearchIndexOptions.SectionName));

        services.AddSingleton<ISearchIndex>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SearchIndexOptions>>().Value;
            var logger = sp.GetService<ILogger<InMemoryFixtureIndex>>();
            var seed = LoadFixture(options.FixtureName);

            return SearchIndexFactory.Build(options, seed, logger);
        });

        return services;
    }

    public static IServiceCollection AddIngestion(this IServiceCollection services)
    {
        services.AddSingleton<IListingCatalog>(new InMemoryListingCatalog());
        services.AddSingleton<IEventIdempotencyStore, InMemoryEventIdempotencyStore>();
        services.AddSingleton<IIngestionConsumer, IngestionConsumer>();

        return services;
    }

    public static IServiceCollection AddSearchPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SearchOptions>>().Value);

        // Registration order IS the pipeline order (SearchOrchestrator resolves
        // IEnumerable<ISearchStage> and runs it as given) — SPEC's stage list, in code.
        services.AddSingleton<ISearchStage, QueryParserStage>();
        services.AddSingleton<ISearchStage, FilterResolverStage>();
        services.AddSingleton<ISearchStage, LexicalRetrieverStage>();
        services.AddSingleton<ISearchStage, VectorRetrieverStage>();
        services.AddSingleton<ISearchStage, HybridRankerStage>();
        services.AddSingleton<ISearchStage, ResponseAssemblerStage>();

        services.AddSingleton<ISearchOrchestrator, SearchOrchestrator>();

        return services;
    }

    public static IServiceCollection AddListingSearchService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSearchIndex(configuration);
        services.AddIngestion();
        services.AddSearchPipeline(configuration);

        return services;
    }

    private static IReadOnlyList<ListingDocument> LoadFixture(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Data", $"{fixtureName}.yaml");

        if (!File.Exists(path))
        {
            // P8: an absent fixture degrades to an empty, working catalogue rather
            // than a startup crash — a request against it returns zero results, not
            // a 500. The scenarios that need this fixture to exist assert on the
            // fixture directly (evals/README.md), not on this fallback.
            return [];
        }

        return CatalogueFixtureLoader.Load(path).Listings;
    }
}
