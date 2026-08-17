using ListingSearch.Service.Search;

namespace ListingSearch.Service.Tests;

public class SearchIndexFilterTests
{
    private static readonly ListingDocument Listing = new(
        ListingId: "lst-9001",
        Title: "Test listing",
        Description: "A listing used only by this test.",
        City: "Zurich",
        PriceChf: 800000,
        Rooms: 3,
        Status: ListingStatus.Active,
        OwnerId: "own-001",
        ListedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    private static SearchIndexFilter Filter(
        decimal? minPrice = null, decimal? maxPrice = null, string? city = null,
        decimal? minRooms = null, decimal? maxRooms = null, ListingStatus[]? statuses = null) =>
        new(minPrice, maxPrice, city, minRooms, maxRooms, statuses ?? [ListingStatus.Active]);

    [Fact]
    public void Admits_a_listing_with_no_filters_set_beyond_status()
    {
        Assert.True(Filter().Admits(Listing));
    }

    [Fact]
    public void Rejects_a_status_not_in_AllowedStatuses()
    {
        var delisted = Listing with { Status = ListingStatus.Delisted };
        Assert.False(Filter().Admits(delisted));
    }

    [Fact]
    public void Rejects_a_price_above_the_maximum()
    {
        Assert.False(Filter(maxPrice: 700000).Admits(Listing));
        Assert.True(Filter(maxPrice: 900000).Admits(Listing));
    }

    [Fact]
    public void Rejects_a_price_below_the_minimum()
    {
        Assert.False(Filter(minPrice: 900000).Admits(Listing));
        Assert.True(Filter(minPrice: 700000).Admits(Listing));
    }

    [Fact]
    public void City_comparison_is_case_insensitive()
    {
        Assert.True(Filter(city: "zurich").Admits(Listing));
        Assert.True(Filter(city: "ZURICH").Admits(Listing));
        Assert.False(Filter(city: "Geneva").Admits(Listing));
    }

    [Fact]
    public void Rejects_a_room_count_outside_the_requested_range()
    {
        Assert.False(Filter(minRooms: 4).Admits(Listing));
        Assert.False(Filter(maxRooms: 2).Admits(Listing));
        Assert.True(Filter(minRooms: 2, maxRooms: 4).Admits(Listing));
    }

    [Fact]
    public void AllowedStatuses_never_defaults_to_including_delisted_or_expired()
    {
        // A filter that forgot to set AllowedStatuses at all (empty list) admits nothing —
        // there is no implicit "everything is fine" default, per SPEC C-1.
        var noStatusesAllowed = Filter(statuses: []);
        Assert.False(noStatusesAllowed.Admits(Listing));
    }
}
