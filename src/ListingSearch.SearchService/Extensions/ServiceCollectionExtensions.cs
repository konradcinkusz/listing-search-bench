using System.Threading.RateLimiting;
using ListingSearch.SearchService.Endpoints;
using ListingSearch.SearchService.Ingestion;
using ListingSearch.SearchService.Pipeline;
using ListingSearch.SearchService.Pipeline.Embedding;
using ListingSearch.SearchService.Pipeline.Stages;
using ListingSearch.SearchService.Search;
using ListingSearch.SearchService.Search.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ListingSearch.SearchService.Extensions;

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

            // Falls back to a fresh DeterministicEmbeddingProvider if AddSearchPipeline
            // (which registers IEmbeddingProvider) was never called against this
            // collection — SearchIndexFactory.Build stays usable on its own, the same
            // way it already is for every caller that isn't this composition root.
            var embeddingProvider = sp.GetService<IEmbeddingProvider>();

            return SearchIndexFactory.Build(options, seed, logger, embeddingProvider: embeddingProvider);
        });

        services.AddHealthChecks().AddCheck<SearchIndexHealthCheck>("search_index");

        return services;
    }

    public static IServiceCollection AddSearchRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SearchRateLimitOptions>(configuration.GetSection(SearchRateLimitOptions.SectionName));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(SearchRateLimitOptions.PolicyName, httpContext =>
            {
                var options = httpContext.RequestServices.GetRequiredService<IOptions<SearchRateLimitOptions>>().Value;
                var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.PermitLimit,
                    Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }

    public static IServiceCollection AddIngestion(this IServiceCollection services)
    {
        services.AddSingleton<IListingCatalog>(new InMemoryListingCatalog());
        services.AddSingleton<IEventIdempotencyStore, InMemoryEventIdempotencyStore>();
        services.AddSingleton<IPendingEventBuffer, InMemoryPendingEventBuffer>();
        services.AddSingleton<IDeadLetterSink, InMemoryDeadLetterSink>();
        services.AddSingleton<IIngestionConsumer, IngestionConsumer>();

        return services;
    }

    public static IServiceCollection AddSearchPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SearchOptions>>().Value);

        // The seam a real embedding model would sit behind (D-1) — registered here,
        // not in AddSearchIndex, because the eval harness builds the pipeline through
        // this method directly (ScenarioRunner) without ever calling AddSearchIndex.
        services.AddSingleton<IEmbeddingProvider, DeterministicEmbeddingProvider>();

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
        services.AddSearchRateLimiting(configuration);

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
