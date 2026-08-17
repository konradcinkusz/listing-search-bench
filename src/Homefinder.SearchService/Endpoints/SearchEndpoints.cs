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
                Top: body.Top ?? 10);

            var response = await orchestrator.RunAsync(request, cancellationToken).ConfigureAwait(false);

            return Results.Ok(response);
        });

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
