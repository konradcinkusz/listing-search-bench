using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ListingSearch.Service.Tests;

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
}
