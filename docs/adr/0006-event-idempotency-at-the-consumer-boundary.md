# ADR-0006: Event idempotency at the consumer boundary

- **Status**: Accepted
- **Date**: 2026-08-17
- **Phase**: 1
- **Relates to**: SPEC C-6, P11 (anti-corruption at the edge)

## Context

`listing.published`, `listing.price_changed` and `listing.delisted` events arrive
over a queue, and queues redeliver by design — a consumer that crashes after
applying an event but before acknowledging it will see that event again. Something
has to guarantee an event is applied at most once regardless, and there are two
places that guarantee could live: the producer's discipline, or the consumer's own
boundary.

## Decision

`IngestionConsumer` owns idempotency, through `IEventIdempotencyStore`. Every event
is `TryReserve`d by its `event_id` before anything is applied; a reservation that
fails means the event has already been applied (or is being applied concurrently)
and the event is reported `ingestion.duplicate_ignored`, not re-applied. A
reservation whose apply then fails is released, so a corrected replay under the same
`event_id` is not permanently locked out (SPEC §7.2). `ISearchIndex` and
`IListingCatalog` carry no idempotency logic of their own — they trust the consumer
that already checked.

There is also, deliberately, no HTTP route that reaches `ISearchIndex.IndexAsync` or
`DeleteAsync` other than through this consumer (SPEC §2.1, O-1) — the anti-corruption
seam this ADR builds is only as strong as being the *only* way in.

## Alternatives considered

### "The producer promises no duplicates"

**Why it is attractive:** Simpler consumer, no registry to maintain, and matches how
a well-behaved publisher is supposed to behave.

**Why it lost:** A promise from outside the service boundary is not a property of
the service — it is a hope. The moment a producer's own retry logic, a
network partition, or an operator's manual replay violates that promise once, C-6
is silently broken, and nothing in this repository would have noticed. The whole
premise of a hard constraint (SPEC §4) is that it does not depend on every upstream
system's discipline holding.

### A database-level unique constraint on `event_id`

**Why it is attractive:** Idempotency enforced by the storage layer itself, closer
to "impossible to get wrong" than application code re-checking a dictionary.

**Why it lost:** This repository's system of record (`InMemoryListingCatalog`) is
in-memory by design (ADR-0002's zero-credential default) — there is no database to
put a constraint on yet. The idempotency registry is written as its own seam
(`IEventIdempotencyStore`) precisely so a real implementation (a unique index in
whatever store eventually backs `IListingCatalog`) can replace
`InMemoryEventIdempotencyStore` without the consumer's logic changing at all.

## Consequences

**What this makes easy:** `adv-002` (SPEC's replay-resurrection scenario) and
`deg-004` (a failed event not permanently blocking its own retry) are both testable
in-process, with no real queue and no real database.

**What this makes hard:** Multi-instance deployment without a shared, persistent
`IEventIdempotencyStore` — two replicas each holding their own
`InMemoryEventIdempotencyStore` would each independently apply the same event once,
which is `N` applications for `N` replicas rather than one. This POC runs single-instance.

**What we accept:** No real message queue exists yet (D-3) — `IngestionConsumer` is
exercised directly, by the eval harness and by a hosted background service's future
implementation alike, never by a queue this repository has actually run against.

## Revisit when

A real queue and a persistent `IEventIdempotencyStore` implementation exist, and
multi-instance deployment is in scope.
