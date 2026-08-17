using Homefinder.SearchService.Ingestion;
using Homefinder.SearchService.Search;
using Homefinder.SearchService.Search.Fixtures;

namespace Homefinder.SearchService.Tests;

public class IngestionConsumerTests
{
    private static (IngestionConsumer Consumer, InMemoryListingCatalog Catalog, InMemoryFixtureIndex Index) Build()
    {
        var catalog = new InMemoryListingCatalog();
        var index = new InMemoryFixtureIndex();
        var idempotency = new InMemoryEventIdempotencyStore();

        return (new IngestionConsumer(index, catalog, idempotency), catalog, index);
    }

    private static ListingEventPayload FullPayload(string listingId) => new(
        listingId,
        Title: "Test apartment",
        Description: "A test apartment used only by this test.",
        City: "Zurich",
        PriceChf: 700000,
        Rooms: 3,
        OwnerId: "own-001");

    [Fact]
    public async Task Publishing_a_new_listing_indexes_it_as_active()
    {
        var (consumer, catalog, _) = Build();

        var outcome = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-1", ListingEventType.Published, FullPayload("lst-9201"), DateTimeOffset.UtcNow));

        Assert.Equal(IngestionOutcome.Applied, outcome);
        Assert.Equal(ListingStatus.Active, catalog.Find("lst-9201")!.Status);
    }

    [Fact]
    public async Task The_same_event_id_applied_twice_is_a_duplicate_the_second_time()
    {
        var (consumer, _, _) = Build();
        var envelope = new IngestionEnvelope("evt-2", ListingEventType.Published, FullPayload("lst-9202"), DateTimeOffset.UtcNow);

        var first = await consumer.ConsumeAsync(envelope);
        var second = await consumer.ConsumeAsync(envelope);

        Assert.Equal(IngestionOutcome.Applied, first);
        Assert.Equal(IngestionOutcome.DuplicateIgnored, second);
    }

    [Fact]
    public async Task A_price_changed_event_patches_only_the_price()
    {
        var (consumer, catalog, _) = Build();
        await consumer.ConsumeAsync(new IngestionEnvelope("evt-3a", ListingEventType.Published, FullPayload("lst-9203"), DateTimeOffset.UtcNow));

        await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-3b", ListingEventType.PriceChanged, new ListingEventPayload("lst-9203", PriceChf: 650000), DateTimeOffset.UtcNow));

        var listing = catalog.Find("lst-9203")!;
        Assert.Equal(650000, listing.PriceChf);
        Assert.Equal("Test apartment", listing.Title); // untouched by the patch
    }

    [Fact]
    public async Task A_delisted_event_flips_status_without_touching_other_fields()
    {
        var (consumer, catalog, _) = Build();
        await consumer.ConsumeAsync(new IngestionEnvelope("evt-4a", ListingEventType.Published, FullPayload("lst-9204"), DateTimeOffset.UtcNow));

        await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-4b", ListingEventType.Delisted, new ListingEventPayload("lst-9204"), DateTimeOffset.UtcNow));

        var listing = catalog.Find("lst-9204")!;
        Assert.Equal(ListingStatus.Delisted, listing.Status);
        Assert.Equal(700000, listing.PriceChf);
    }

    [Fact]
    public async Task A_price_changed_event_for_an_unknown_listing_fails_without_creating_one()
    {
        var (consumer, catalog, _) = Build();

        var outcome = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-5", ListingEventType.PriceChanged, new ListingEventPayload("lst-9999", PriceChf: 500000), DateTimeOffset.UtcNow));

        Assert.Equal(IngestionOutcome.Failed, outcome);
        Assert.Null(catalog.Find("lst-9999"));
    }

    [Fact]
    public async Task A_published_event_missing_a_required_field_fails_and_releases_its_event_id()
    {
        var (consumer, catalog, _) = Build();
        var incomplete = new ListingEventPayload("lst-9205", Title: "Test", City: "Zurich", PriceChf: null, Rooms: 3, OwnerId: "own-001");

        var failedAttempt = await consumer.ConsumeAsync(new IngestionEnvelope("evt-6", ListingEventType.Published, incomplete, DateTimeOffset.UtcNow));
        Assert.Equal(IngestionOutcome.Failed, failedAttempt);
        Assert.Null(catalog.Find("lst-9205"));

        var correctedRetry = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-6", ListingEventType.Published, FullPayload("lst-9205"), DateTimeOffset.UtcNow));

        Assert.Equal(IngestionOutcome.Applied, correctedRetry);
    }
}
