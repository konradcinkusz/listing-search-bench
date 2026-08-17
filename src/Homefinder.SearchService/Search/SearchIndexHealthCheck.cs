using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Homefinder.SearchService.Search;

/// <summary>
/// Wires <see cref="ISearchIndex.HealthAsync"/> into <c>GET /health</c> — readiness,
/// deliberately untagged so it never affects <c>GET /alive</c>
/// (<c>Homefinder.ServiceDefaults.Extensions</c>): a struggling backend should take
/// this instance out of the load balancer, not have Kubernetes restart it. Before
/// this existed, an unreachable Elasticsearch cluster had no way to fail the
/// readiness probe at all — the check just wasn't there.
/// </summary>
public sealed class SearchIndexHealthCheck(ISearchIndex index) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var health = await index.HealthAsync(cancellationToken).ConfigureAwait(false);

        return health.Healthy
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy(health.Detail ?? "search index reported unhealthy");
    }
}
