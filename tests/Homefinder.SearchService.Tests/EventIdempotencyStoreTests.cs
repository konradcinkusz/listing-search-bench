using Homefinder.SearchService.Ingestion;

namespace Homefinder.SearchService.Tests;

public class EventIdempotencyStoreTests
{
    [Fact]
    public void First_reservation_of_an_event_id_succeeds()
    {
        var store = new InMemoryEventIdempotencyStore();
        Assert.True(store.TryReserve("evt-001"));
    }

    [Fact]
    public void A_second_reservation_of_the_same_event_id_fails()
    {
        var store = new InMemoryEventIdempotencyStore();
        store.TryReserve("evt-001");

        Assert.False(store.TryReserve("evt-001"));
    }

    [Fact]
    public void Releasing_a_reservation_allows_it_to_be_claimed_again()
    {
        var store = new InMemoryEventIdempotencyStore();
        store.TryReserve("evt-001");
        store.Release("evt-001");

        Assert.True(store.TryReserve("evt-001"));
    }

    [Fact]
    public void Different_event_ids_do_not_interfere_with_each_other()
    {
        var store = new InMemoryEventIdempotencyStore();

        Assert.True(store.TryReserve("evt-001"));
        Assert.True(store.TryReserve("evt-002"));
    }
}
