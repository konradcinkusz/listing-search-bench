using Homefinder.SearchService.Endpoints;
using Homefinder.SearchService.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHomefinderSearchService(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapSearchEndpoints();

await app.RunAsync().ConfigureAwait(false);

// Exposed for WebApplicationFactory in integration tests.
public partial class Program
{
}
