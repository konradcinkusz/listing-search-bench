using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Homefinder.SearchService.Tests;

/// <summary>
/// Its own <see cref="Factory"/>, configured with a tiny permit limit — deliberately
/// not <see cref="SearchEndpointTests"/>' shared fixture, whose rate-limiter state
/// (partitioned by a caller IP that <c>TestServer</c> reports the same way on every
/// request) would otherwise make a 429 assertion order-dependent on however many other
/// tests already called <c>POST /search</c> against that same fixture.
/// </summary>
public sealed class RateLimitingTests(RateLimitingTests.Factory factory) : IClassFixture<RateLimitingTests.Factory>
{
    [Fact]
    public async Task A_caller_past_the_permit_limit_is_rejected_with_429()
    {
        using var client = factory.CreateClient();

        HttpResponseMessage? last = null;

        for (var i = 0; i < Factory.PermitLimit + 1; i++)
        {
            last = await client.PostAsJsonAsync("/search", new { query = "apartment" });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public const int PermitLimit = 3;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimit:PermitLimit"] = PermitLimit.ToString(CultureInfo.InvariantCulture),
                    ["RateLimit:WindowSeconds"] = "60",
                });
            });
        }
    }
}
