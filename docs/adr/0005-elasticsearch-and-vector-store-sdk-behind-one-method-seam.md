# ADR-0005: The Elasticsearch and vector-store SDK lives behind a five-method seam

- **Status**: Accepted
- **Date**: 2026-08-17
- **Phase**: 1
- **Relates to**: P11 (anti-corruption at the edge), ADR-0005 of `agent-eval-bench`

## Context

`Elastic.Clients.Elasticsearch` has its own query DSL, its own descriptor types, its
own response shapes. Something in this repository has to speak that dialect; the
question is how much of the repository is allowed to.

## Decision

Exactly one file, `Search/Elasticsearch/ElasticsearchIndex.cs`, imports
`Elastic.Clients.Elasticsearch`. Everything else — the pipeline stages, the eval
harness, `InMemoryFixtureIndex` — depends only on `ISearchIndex`, a five-method
interface (`QueryAsync`, `VectorQueryAsync`, `IndexAsync`, `DeleteAsync`,
`HealthAsync`) with no vendor type anywhere in its signatures. The same discipline
applies to the fixture-backed default: `InMemoryFixtureIndex` implements the
identical interface with none of the same types, and the pipeline cannot tell which
one it is talking to.

## Alternatives considered

### Let pipeline stages call the Elasticsearch client directly for the vector path only

**Why it is attractive:** Less indirection for the one path (vector search) whose
query shape (`knn`, filters, candidate counts) is genuinely Elasticsearch-specific
and does not obviously generalise.

**Why it lost:** It would mean two different retrieval stages have two different
kinds of dependency — one on an interface, one on a concrete SDK — and the mutation
pass's `Replace<TOriginal, TMutant>` pattern (SPEC §8.6) depends on every stage
being swappable the same way. It would also make `VectorRetrieverStage` untestable
without a real cluster, which is exactly the property ADR-0002 exists to avoid.

### Test `ElasticsearchIndex` against a real cluster in CI

**Why it is attractive:** The only way to know the query DSL is actually correct,
rather than merely compiling — untestable code is where SDK-shape bugs hide until
production.

**Why it lost:** No CI job in this repository has a cluster to point it at
(ADR-0002), so this would mean either introducing one (rejected there) or writing
tests that could not run. `ElasticsearchIndex.cs`'s correctness is therefore a
documented, dated gap (`docs/DEVIATIONS.md` D-4) rather than a claim backed by a
passing test — the same honesty `agent-eval-bench` states for its MCP adapter, which
is "written from the protocol and the SDK's documentation" and untested against a
live server.

## Consequences

**What this makes easy:** `SearchIndexFactory` swaps `InMemoryFixtureIndex` for
`ElasticsearchIndex` behind one `if`, and nothing downstream notices or needs to.

**What this makes hard:** Catching an Elasticsearch query-DSL mistake before a human
runs it against a real cluster for the first time — the compiler catches a wrong
method name, not a wrong `knn` parameter that compiles and returns nothing.

**What we accept:** `ElasticsearchIndex.cs` exists, compiles, and has never
answered a real query.

## Revisit when

A keyed CI job with a disposable Elasticsearch cluster exists (closing D-4), at
which point this file gets its own integration tests.
