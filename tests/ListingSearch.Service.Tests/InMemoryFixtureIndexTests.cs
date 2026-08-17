using ListingSearch.Service.Search;
using ListingSearch.Service.Search.Fixtures;

namespace ListingSearch.Service.Tests;

public class InMemoryFixtureIndexTests
{
    private static readonly SearchIndexFilter ActiveOnly = new(null, null, null, null, null, [ListingStatus.Active]);

    private static ListingDocument Listing(string id, string title, ListingStatus status = ListingStatus.Active) => new(
        id, title, $"{title} — a description containing the same words.", "Zurich", 800000, 3, status, "own-001",
        DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public async Task DeleteAsync_removes_a_listing_so_it_is_never_returned_again()
    {
        var index = new InMemoryFixtureIndex([Listing("lst-9101", "modern apartment")]);

        var before = await index.QueryAsync(ActiveOnly, ["modern"], 10);
        Assert.Single(before.Hits);

        await index.DeleteAsync("lst-9101");

        var after = await index.QueryAsync(ActiveOnly, ["modern"], 10);
        Assert.Empty(after.Hits);
    }

    [Fact]
    public async Task IndexAsync_upserts_rather_than_duplicating()
    {
        var index = new InMemoryFixtureIndex([Listing("lst-9102", "modern apartment")]);

        await index.IndexAsync(Listing("lst-9102", "modern apartment, repriced"));

        Assert.Equal(1, index.Count);
    }

    [Fact]
    public async Task A_listing_failing_the_filter_is_reported_as_rejected_not_silently_dropped()
    {
        var index = new InMemoryFixtureIndex([Listing("lst-9103", "modern apartment", ListingStatus.Delisted)]);

        var result = await index.QueryAsync(ActiveOnly, ["modern"], 10);

        Assert.Empty(result.Hits);
        Assert.Contains("lst-9103", result.Rejected);
    }

    [Fact]
    public async Task HealthAsync_reports_healthy_with_no_faults_injected()
    {
        var index = new InMemoryFixtureIndex([]);
        var health = await index.HealthAsync();

        Assert.True(health.Healthy);
    }
}
