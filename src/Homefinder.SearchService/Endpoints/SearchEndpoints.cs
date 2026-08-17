using Homefinder.SearchService.Pipeline;
using Homefinder.SearchService.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Homefinder.SearchService.Endpoints;

/// <summary>
/// The one HTTP surface this service exposes for reads. There is deliberately no
/// write route anywhere in this file, nor in any other — SPEC §2.1, O-1.
/// </summary>
public static class SearchEndpoints
{
    /// <summary>
    /// The largest page size a caller may request. This is a request-shape bound, not
    /// a ranking behaviour — it does not change what SPEC.md describes, only how much
    /// of it one call may ask for at once, so it belongs here rather than in the
    /// pipeline. <see cref="SearchOptions.CandidatePoolSize"/> already caps how many
    /// candidates ever exist to return; this caps the request, independently, so that
    /// guarantee is never the only thing standing between a caller and an unbounded
    /// response.
    /// </summary>
    public const int MaxTop = 50;

    public static WebApplication MapSearchEndpoints(this WebApplication app)
    {
        app.MapPost("/search", async (SearchRequestBody body, ISearchOrchestrator orchestrator, CancellationToken cancellationToken) =>
        {
            var request = new SearchRequest(
                QueryText: body.Query ?? "",
                MinPrice: body.MinPrice,
                MaxPrice: body.MaxPrice,
                City: body.City,
                MinRooms: body.MinRooms,
                MaxRooms: body.MaxRooms,
                SoftMaxPrice: body.SoftMaxPrice,
                Sort: body.Sort ?? SortOrder.Relevance,
                Top: Math.Clamp(body.Top ?? 10, 1, MaxTop));

            var response = await orchestrator.RunAsync(request, cancellationToken).ConfigureAwait(false);

            return Results.Ok(response);
        })
        .RequireRateLimiting(SearchRateLimitOptions.PolicyName);

        return app;
    }

    public sealed record SearchRequestBody(
        string? Query,
        decimal? MinPrice = null,
        decimal? MaxPrice = null,
        string? City = null,
        decimal? MinRooms = null,
        decimal? MaxRooms = null,
        decimal? SoftMaxPrice = null,
        SortOrder? Sort = null,
        int? Top = null);
}
