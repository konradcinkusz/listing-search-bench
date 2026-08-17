using System.Net;
using System.Net.Http.Json;
using Homefinder.SearchService.Search;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Homefinder.SearchService.Tests;

/// <summary>
/// SPEC O-1: there is no HTTP route that writes to the index. This is not a
/// convention to remember — it is checked here against the real, fully composed
/// application, the same way a live request would reach it.
/// </summary>
public sealed class NoHttpRouteReachesTheIndexTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("POST", "/listings")]
    [InlineData("PUT", "/listings/lst-1001")]
    [InlineData("DELETE", "/listings/lst-1001")]
    [InlineData("POST", "/index")]
    [InlineData("POST", "/ingest")]
    [InlineData("POST", "/search/lst-1001")]
    public async Task No_write_shaped_route_exists(string method, string path)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Search_does_not_accept_GET()
    {
        using var client = factory.CreateClient();

        var getResponse = await client.GetAsync("/search");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResponse.StatusCode);
    }
}

public sealed class SearchEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_search_request_returns_a_completed_or_degraded_response_never_a_server_error()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/search", new { query = "modern apartment zurich", city = "Zurich", top = 5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"outcome\"", body);
    }

    [Theory]
    [InlineData(999999)]
    [InlineData(-5)]
    [InlineData(0)]
    public async Task An_out_of_range_top_is_clamped_rather_than_erroring(int top)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/search", new { query = "apartment", top });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>
/// Its own <see cref="WebApplicationFactory{TEntryPoint}"/>, with <see cref="ISearchIndex"/>
/// replaced after the fact — deliberately not <see cref="SearchEndpointTests"/>' shared
/// fixture, so a simulated outage here can never leak into another test's expectation
/// that the index is healthy.
/// </summary>
public sealed class SearchIndexHealthCheckTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task An_unhealthy_index_fails_readiness_but_not_liveness()
    {
        using var unhealthyFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ISearchIndex>(new UnhealthyIndex())));
        using var client = unhealthyFactory.CreateClient();

        var readiness = await client.GetAsync("/health");
        var liveness = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
    }

    private sealed class UnhealthyIndex : ISearchIndex
    {
        public ValueTask<IndexQueryResult> QueryAsync(
            SearchIndexFilter filter, IReadOnlyList<string> tokens, int topN, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(IndexQueryResult.Empty);

        public ValueTask<IndexQueryResult> VectorQueryAsync(
            SearchIndexFilter filter, IReadOnlyList<double> queryEmbedding, int topN, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(IndexQueryResult.Empty);

        public ValueTask IndexAsync(ListingDocument document, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DeleteAsync(string listingId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask<IndexHealth> HealthAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new IndexHealth(false, "simulated outage"));
    }
}
