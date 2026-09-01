using ListingSearch.Service.Ingestion;
using ListingSearch.Service.Search;
using ListingSearch.Service.Search.Fixtures;

namespace ListingSearch.Service.Tests;

public class IngestionConsumerTests
{
    private static (
        IngestionConsumer Consumer,
        InMemoryListingCatalog Catalog,
        InMemoryFixtureIndex Index,
        InMemoryDeadLetterSink DeadLetters) Build(int maxPendingPerListing = 3)
    {
        var catalog = new InMemoryListingCatalog();
        var index = new InMemoryFixtureIndex();
        var idempotency = new InMemoryEventIdempotencyStore();
        var pending = new InMemoryPendingEventBuffer(maxPendingPerListing);
        var deadLetters = new InMemoryDeadLetterSink();

        return (new IngestionConsumer(index, catalog, idempotency, pending, deadLetters), catalog, index, deadLetters);
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
        var (consumer, catalog, _, _) = Build();

        var outcome = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-1", ListingEventType.Published, FullPayload("lst-9201"), DateTimeOffset.UtcNow));

        Assert.Equal(IngestionOutcome.Applied, outcome);
        Assert.Equal(ListingStatus.Active, catalog.Find("lst-9201")!.Status);
    }

    [Fact]
    public async Task The_same_event_id_applied_twice_is_a_duplicate_the_second_time()
    {
        var (consumer, _, _, _) = Build();
        var envelope = new IngestionEnvelope("evt-2", ListingEventType.Published, FullPayload("lst-9202"), DateTimeOffset.UtcNow);

        var first = await consumer.ConsumeAsync(envelope);
        var second = await consumer.ConsumeAsync(envelope);

        Assert.Equal(IngestionOutcome.Applied, first);
        Assert.Equal(IngestionOutcome.DuplicateIgnored, second);
    }

    [Fact]
    public async Task A_price_changed_event_patches_only_the_price()
    {
        var (consumer, catalog, _, _) = Build();
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
        var (consumer, catalog, _, _) = Build();
        await consumer.ConsumeAsync(new IngestionEnvelope("evt-4a", ListingEventType.Published, FullPayload("lst-9204"), DateTimeOffset.UtcNow));

        await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-4b", ListingEventType.Delisted, new ListingEventPayload("lst-9204"), DateTimeOffset.UtcNow));

        var listing = catalog.Find("lst-9204")!;
        Assert.Equal(ListingStatus.Delisted, listing.Status);
        Assert.Equal(700000, listing.PriceChf);
    }

    [Fact]
    public async Task A_price_changed_event_for_a_not_yet_published_listing_is_deferred_without_creating_one()
    {
        var (consumer, catalog, _, _) = Build();

        var outcome = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-5", ListingEventType.PriceChanged, new ListingEventPayload("lst-9999", PriceChf: 500000), DateTimeOffset.UtcNow));

        Assert.Equal(IngestionOutcome.Deferred, outcome);
        Assert.Null(catalog.Find("lst-9999"));
    }

    [Fact]
    public async Task A_deferred_price_changed_event_replays_once_the_matching_published_event_arrives()
    {
        var (consumer, catalog, _, _) = Build();

        var deferred = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-5b", ListingEventType.PriceChanged, new ListingEventPayload("lst-9206", PriceChf: 690000), DateTimeOffset.UtcNow));
        Assert.Equal(IngestionOutcome.Deferred, deferred);

        var applied = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-5a", ListingEventType.Published, FullPayload("lst-9206"), DateTimeOffset.UtcNow));
        Assert.Equal(IngestionOutcome.Applied, applied);

        var listing = catalog.Find("lst-9206")!;
        Assert.Equal(690000, listing.PriceChf); // the deferred price change, not the published payload's own 700000
    }

    [Fact]
    public async Task A_deferred_events_own_event_id_is_still_reserved_so_a_replay_of_it_is_a_duplicate()
    {
        var (consumer, _, _, _) = Build();
        var deferredTwice = new IngestionEnvelope(
            "evt-5c", ListingEventType.PriceChanged, new ListingEventPayload("lst-9207", PriceChf: 500000), DateTimeOffset.UtcNow);

        var first = await consumer.ConsumeAsync(deferredTwice);
        var second = await consumer.ConsumeAsync(deferredTwice);

        Assert.Equal(IngestionOutcome.Deferred, first);
        Assert.Equal(IngestionOutcome.DuplicateIgnored, second);
    }

    [Fact]
    public async Task A_price_changed_event_missing_its_own_price_fails_immediately_rather_than_deferring()
    {
        var (consumer, _, _, deadLetters) = Build();

        var outcome = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-5d", ListingEventType.PriceChanged, new ListingEventPayload("lst-9999"), DateTimeOffset.UtcNow));

        Assert.Equal(IngestionOutcome.Failed, outcome);
        Assert.Empty(deadLetters.Entries); // failed, not dead-lettered — those are different outcomes
    }

    [Fact]
    public async Task A_listings_pending_buffer_dead_letters_once_it_fills_up_with_no_published_event()
    {
        var (consumer, _, _, deadLetters) = Build(maxPendingPerListing: 2);

        var first = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-6a", ListingEventType.PriceChanged, new ListingEventPayload("lst-9208", PriceChf: 500000), DateTimeOffset.UtcNow));
        var second = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-6b", ListingEventType.Delisted, new ListingEventPayload("lst-9208"), DateTimeOffset.UtcNow));
        var third = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-6c", ListingEventType.PriceChanged, new ListingEventPayload("lst-9208", PriceChf: 480000), DateTimeOffset.UtcNow));

        Assert.Equal(IngestionOutcome.Deferred, first);
        Assert.Equal(IngestionOutcome.Deferred, second);
        Assert.Equal(IngestionOutcome.DeadLettered, third);

        var entry = Assert.Single(deadLetters.Entries);
        Assert.Equal("evt-6c", entry.EventId);
        Assert.Equal("lst-9208", entry.ListingId);
    }

    [Fact]
    public async Task A_dead_lettered_events_event_id_is_released_so_a_corrected_replay_is_not_a_duplicate()
    {
        var (consumer, catalog, _, _) = Build(maxPendingPerListing: 0);

        var deadLettered = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-7", ListingEventType.Delisted, new ListingEventPayload("lst-9209"), DateTimeOffset.UtcNow));
        Assert.Equal(IngestionOutcome.DeadLettered, deadLettered);

        // The same event_id, replayed after the listing finally gets published — not
        // a duplicate, because dead-lettering released the reservation (SPEC §7.2).
        await consumer.ConsumeAsync(new IngestionEnvelope("evt-8", ListingEventType.Published, FullPayload("lst-9209"), DateTimeOffset.UtcNow));
        var retried = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-7", ListingEventType.Delisted, new ListingEventPayload("lst-9209"), DateTimeOffset.UtcNow));

        Assert.Equal(IngestionOutcome.Applied, retried);
        Assert.Equal(ListingStatus.Delisted, catalog.Find("lst-9209")!.Status);
    }

    [Fact]
    public async Task A_published_event_missing_a_required_field_fails_and_releases_its_event_id()
    {
        var (consumer, catalog, _, _) = Build();
        var incomplete = new ListingEventPayload("lst-9205", Title: "Test", City: "Zurich", PriceChf: null, Rooms: 3, OwnerId: "own-001");

        var failedAttempt = await consumer.ConsumeAsync(new IngestionEnvelope("evt-6", ListingEventType.Published, incomplete, DateTimeOffset.UtcNow));
        Assert.Equal(IngestionOutcome.Failed, failedAttempt);
        Assert.Null(catalog.Find("lst-9205"));

        var correctedRetry = await consumer.ConsumeAsync(new IngestionEnvelope(
            "evt-6", ListingEventType.Published, FullPayload("lst-9205"), DateTimeOffset.UtcNow));

        Assert.Equal(IngestionOutcome.Applied, correctedRetry);
    }
}
