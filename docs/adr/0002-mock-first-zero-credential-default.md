# ADR-0002: Mock index first, zero credentials by default

- **Status**: Accepted
- **Date**: 2026-08-17
- **Phase**: 1
- **Relates to**: P8 (optional dependencies degrade to a working fallback), ADR-0002 of `agent-eval-bench`

## Context

`ISearchIndex` has two implementations: `InMemoryFixtureIndex` (in-process, zero
credentials) and `ElasticsearchIndex` (a real cluster, dev-only). Something has to be
the default a fresh clone runs against, and that choice decides who can run this
repository's suite: everyone, or only someone who first stands up infrastructure.

## Decision

`SearchIndex:Mode` defaults to `Fixture`, never `Elasticsearch`. `dotnet test
--project evals/ListingSearch.Evals` runs the entire Layer 1 corpus and mutation pass
against `InMemoryFixtureIndex` with no cluster, no network call, and no credential of
any kind. `SearchIndexFactory` falls back to the fixture index with a logged warning
if `Mode=Elasticsearch` is set without `ElasticsearchUri` (P8), rather than failing
startup.

## Alternatives considered

### Live Elasticsearch as the default, `docker-compose up` as a prerequisite

**Why it is attractive:** Every eval would run against the real retrieval engine
this repository is ultimately about, closing D-4 by construction rather than leaving
it a stated gap.

**Why it lost:** It couples "can I read this repository's evidence" to "do I have
Docker running and eight seconds to spare for a cluster to come up", and it couples
CI's reproducibility to a container's health, which is a second thing that can be
red for reasons that have nothing to do with the pipeline under test. Every PR would
also need a place to run that container, which is infrastructure this repository
does not otherwise need. `agent-eval-bench`'s ADR-0002 makes the identical
trade for the same reason on the workforce-tools side.

### One implementation only, delete `ElasticsearchIndex`

**Why it is attractive:** No dev-only code path that CI never exercises, no D-4 to
write down.

**Why it lost:** The whole point of `ISearchIndex` as a seam (ADR-0004) is that a
real backend can be swapped in without touching the pipeline. Deleting the one real
implementation would make that claim untested by omission rather than tested and
caveated.

## Consequences

**What this makes easy:** `git clone && dotnet test` runs the full suite with an
empty `.env`. Anyone reviewing this repository's evidence can reproduce it without
asking for access to anything.

**What this makes hard:** Claiming anything about Elasticsearch's actual ranking
behaviour, latency, or shard failure modes at scale — `docs/DEVIATIONS.md` D-2 and
D-4 state this rather than letting a green suite imply it.

**What we accept:** `FaultInjectingSearchIndex` simulates degradation
(`evals/ListingSearch.Evals/World/FaultInjectingSearchIndex.cs`) rather than observing
it on a real cluster with real shard topology.

## Revisit when

A keyed CI job with a disposable Elasticsearch cluster exists and D-4 is ready to
close.
