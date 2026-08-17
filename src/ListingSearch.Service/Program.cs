using ListingSearch.Service.Endpoints;
using ListingSearch.Service.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddListingSearchService(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapSearchEndpoints();

await app.RunAsync().ConfigureAwait(false);

// Exposed for WebApplicationFactory in integration tests.
public partial class Program
{
}
