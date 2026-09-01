using ListingSearch.SearchService.Endpoints;
using ListingSearch.SearchService.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddListingSearchService(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseRateLimiter();
app.MapSearchEndpoints();

await app.RunAsync().ConfigureAwait(false);

// Exposed for WebApplicationFactory in integration tests.
public partial class Program
{
}
