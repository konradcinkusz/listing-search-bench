# The ListingSearch service — behaviour specification

- **Service slug**: `listing-search-service`
- **Spec version**: 1.0.0
- **Status**: Accepted — this is the contract. Code is measured against it, not the other way round.
- **Date**: 2026-08-17

> **This document exists before the ranking pipeline does.** Nothing in
> `ListingSearch.Service` had been written when version 1.0.0 was accepted.
> That ordering is the method, not a scheduling accident: a ranking change gets
> shipped the way a boost weight gets tuned — casually, in a config diff nobody
> reviews as behaviour — and the spec is what makes such a change reviewable
> ([`AI-EVALS.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/AI-EVALS.md) §2).
>
> If building the pipeline shows a clause here to be wrong, the clause is
> amended **in the same pull request as the code**, with the version bumped and
> the reason recorded. What must not happen is the code quietly becoming the
> specification.

**Versioning.** A behaviour change is a version bump, and the eval suite is
what the bump is measured against, following the discipline
[`AI-EVALS.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/AI-EVALS.md)
§2 states for any LLM- or ranking-backed feature. A spec change with no
scenario change is reviewed with suspicion — it usually means a behaviour was
described but not made checkable.

## Contents

1. [What this service is](#1-what-this-service-is)
1. [Vocabulary](#2-vocabulary)
1. [Expected behaviours](#3-expected-behaviours)
1. [Hard constraints](#4-hard-constraints)
1. [Success criteria and rubric anchors](#5-success-criteria-and-rubric-anchors)
1. [Out of scope](#6-out-of-scope)
1. [Degradation contract](#7-degradation-contract)
1. [How the suite runs](#8-how-the-suite-runs)
1. [Assumptions, and what is deliberately undecided](#9-assumptions)
1. [How this document changes](#10-how-this-document-changes)

---

## 1. What this service is

One capability, scoped narrowly: **a user searches a catalogue of real-estate
listings in free text plus structured filters, and the service returns a
ranked result set that respects every hard constraint the business places on
it — never a listing outside the filter, never a delisted listing, never a
result whose ranking was bought by manipulating the listing's own text.**

The interesting half is the second one. Full-text search over a document
store is a solved problem with a library. What is not solved, and what every
hybrid retrieval system gets wrong somewhere, is a **ranker that combines two
retrieval paths — lexical (BM25-style) and dense vector similarity — without
letting either path become the hole the other path's guarantees leak through.**

Everything in this specification serves that: the behaviours describe
returning the right listings in the right order, the constraints describe
never returning the wrong ones no matter which retrieval path found them, and
the rubrics describe whether, among the allowed results, the best match is
actually on top.

**Non-goals are in [§6](#6-out-of-scope)**, stated as explicit exclusions with
specified behaviour, because an implicit answer is how scope creeps.

## 2. Vocabulary

Behaviours below are testable only if the words in them are pinned. These
definitions are the contract between the spec and the eval harness.

### 2.1 The index boundary

The pipeline reaches the retrieval backend through one internal interface,
`ISearchIndex`. The external dialect — Elasticsearch, or the in-memory fixture
— is normalised at the boundary (P11), so nothing in this document, and
nothing in any scenario, names a vendor.

| Method | Kind | Purpose |
|---|---|---|
| `QueryAsync` | read | Lexical (BM25-style) retrieval, constrained by a `SearchIndexFilter` |
| `VectorQueryAsync` | read | Dense-vector similarity retrieval, constrained by the same filter shape |
| `IndexAsync` | **write** | Upsert one listing document. Reachable only from `IngestionConsumer` |
| `DeleteAsync` | **write** | Remove one listing document. Reachable only from `IngestionConsumer` |
| `HealthAsync` | read | Shard/backend health, used to classify degradation |

**This table is the definition of "write-classified", and it is normative.**
There is deliberately **no HTTP route that calls `IndexAsync` or
`DeleteAsync`.** The only path to a write is `IngestionConsumer`, reading
`listing.published` / `listing.price_changed` / `listing.delisted` events off
a queue. A convenience write endpoint would hand every adversarial scenario a
way around the exact thing this specification tests — the same reasoning
[ADR-6](adr/0006-event-idempotency-at-the-consumer-boundary.md) applies to
*how* a write reaches the index, applied here to *whether* anything besides
`IngestionConsumer` may.

### 2.2 The filter, and why two retrieval paths build it independently

A `SearchIndexFilter` carries the user's hard filters (`MinPrice`, `MaxPrice`,
`City`, `MinRooms`, `MaxRooms`) **and** a mandatory `AllowedStatuses`, which is
`[Active]` on every request the pipeline issues — nothing outside this
document ever widens it.

`FilterResolverStage` computes the filter once, from the request. **Each
retrieval stage then builds its own `SearchIndexFilter` to pass to the
index**, rather than sharing one mutable object end to end. This is stated
because it is the seam a hybrid system's asymmetric bugs live in
([§4](#4-hard-constraints), C-1): a fix applied to the lexical path's filter
construction does not, by any language guarantee, apply itself to the vector
path's. The specification requires both, separately, rather than trusting
that a shared object makes the requirement redundant.

### 2.3 Trace spans and events

The pipeline is instrumented with OpenTelemetry: one span per search request
(`search_turn`), one span per pipeline stage (`search_stage {name}`), one span
per index call (`search_index {operation}`) carrying the filter, the
candidate count, and the outcome. On top of that, these events are part of
the **contract**, not diagnostics:

| Event | Meaning |
|---|---|
| `filter.rejected` | A candidate was excluded by `FilterResolverStage` before any retrieval path saw it |
| `constraint.violated` | `ResponseAssemblerStage` found a candidate that would have exposed an internal identifier, a raw score or an embedding vector, and stripped it independently of what the ranking stage decided — the response-boundary half of C-3 |
| `ingestion.applied` | An ingestion event was applied to the index for the first time |
| `ingestion.duplicate_ignored` | An ingestion event whose `event_id` had already been applied was received again and ignored |
| `ranking.manipulation_ignored` | Instruction-shaped or ranking-manipulative text was found in a listing's own free-text fields and was **not** allowed to influence its score |
| `degradation.noted` | A stage produced partial or no data, and the response says so |

`degradation.noted` carries three attributes, mirroring
[`SERVICE-API-PATTERNS.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/SERVICE-API-PATTERNS.md)
§6's "partial output with a note" rule, made assertable:

| Attribute | Values |
|---|---|
| `degradation.stage` | `lexical_retrieval` · `vector_retrieval` · `filter_resolution` · `ingestion` |
| `degradation.kind` | `timeout` · `shard_unavailable` · `malformed_embedding` · `empty` |

Without these, degradation would be gradeable only by the Layer 2 judge, and
[`AI-EVALS.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/AI-EVALS.md)
§4 is explicit that "most constraint coverage lives here, not in the judge."

### 2.4 One span per logical index call

**An index call is one span, however many transport attempts it took.**
Retries performed by the resilience handler beneath the pipeline appear as
*events on that span* (`attempt`, with an outcome), never as sibling spans —
the same discipline `agent-eval-bench`'s SPEC §2.2.1 states for tool calls,
transferred here so that a scenario asserting `search_index called once` is
never made ambiguous by a transport-level retry underneath it.

### 2.5 Search outcomes

Every search request ends with exactly one `search.turn.outcome` attribute:

`completed` · `degraded`

**A request that returned results built on partial data is `degraded`, not
`completed`.** The failure this precedence guards against is a response that
looks routine while a retrieval path underneath it did not run — the
degradation scenarios in [§7](#7-degradation-contract) are exactly that shape.

### 2.6 Internal identifiers

Two kinds, and [C-3](#4-hard-constraints) covers both:

1. **Public entity ids** — `^lst-[0-9]{3,5}$` (listings), `^own-[0-9]{2,4}$`
   (owners). These are allowed in a response; they are what the client
   addresses a listing by.
1. **Internal index identifiers** — the backend's own document id
   (`^esdoc-[0-9a-f]{8}$` in the fixture and in Elasticsearch alike), the raw
   retrieval score before normalisation, and the embedding vector itself.
   **None of these three ever appears in a response.**

The harness does not pattern-match the third kind by regex over a formatted
number — a raw score is a `double` on an internal record type, and
[C-3](#4-hard-constraints) is evaluated by asserting the **response DTO's
shape** carries no field capable of holding one, not by scanning rendered
JSON for something that looks like a score. A regex over prose is exactly the
`HasText`-shaped failure the estate has already paid for once
([`E2E-ACCEPTANCE-TESTING.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/E2E-ACCEPTANCE-TESTING.md)
§4).

## 3. Expected behaviours

One line each, each testable, each carrying the scenarios that prove it.
Graded by Layer 1 where the property is structural, and additionally by Layer
2 where it is a quality.

| # | Given … the service … | Scenarios |
|---|---|---|
| **B-1** | Resolves structured hard filters (price, city, rooms) before either retrieval path runs, and both paths receive the same resolved filter independently constructed | `hap-001`, `hap-002`, `exc-001` |
| **B-2** | Retrieves lexical candidates with `QueryAsync`, scored by term overlap against title and description | `hap-001`, `hap-003` |
| **B-3** | Retrieves vector candidates with `VectorQueryAsync`, scored by cosine similarity against a deterministic text embedding | `hap-004`, `amb-002` |
| **B-4** | Merges both candidate sets into one ranked list, attributing each result to `lexical`, `vector` or `both`, and records the per-path contribution on the trace | `hap-001`, `hap-005` |
| **B-5** | Where a query names a city plausibly but not exactly ("near the centre", no city given), treats the omission as an **ambiguity to rank around**, not a reason to return nothing | `amb-001` |
| **B-6** | Treats a soft qualifier ("around CHF 1,000,000") as a ranking preference, and a hard qualifier ("under CHF 1,000,000") as a filter that excludes | `amb-003`, `amb-004` |
| **B-7** | Reads a query in French or German against the same corpus and retrieves comparably, because tokenisation is script-based, not vocabulary-based | `amb-005` |
| **B-8** | Ends every request with exactly one `search.turn.outcome`, and a degraded request still returns whatever candidates the working path found | `deg-001`, `deg-002`, `deg-003` |
| **B-9** | Applies an ingestion event to the index exactly once per `event_id`, replays included | `hap-006`, `deg-004` |
| **B-10** | Scans listing free text for instruction-shaped or ranking-manipulative content and reports it via `ranking.manipulation_ignored`, without letting it change the score | `adv-001`, `adv-002` |
| **B-11** | Honours an explicit sort order (price ascending/descending) as an alternative to relevance ranking, applied after filtering | `hap-002` |

## 4. Hard constraints

**Graded by Layer 1, at 100%, hard-blocking the merge.** These are not
aspirations and they are not judged: each is a deterministic property of the
trace. A scenario asserting one of these carries `gate: constraint`.

| # | Constraint | Why it is a constraint rather than a behaviour |
|---|---|---|
| **C-1** | No listing with status `delisted` or `expired` ever appears in a response, or as a candidate on either retrieval path, regardless of which path would otherwise have surfaced it | The whole premise of a hybrid system with two independent code paths: a guarantee that holds on one path and not the other is not a guarantee, it is a coin flip on which path a given query happens to favour |
| **C-2** | A user's hard filters (price, location, rooms) are absolute; no ranking or boosting logic ever admits a candidate outside them | Boosting is a ranking decision. A filter is not one — it is the query's own scope, and a ranker that reaches outside it has answered a different question than the one asked |
| **C-3** | No internal index identifier, raw retrieval score, or embedding vector ever appears in a response | An information leak, and — for the score specifically — a contract nobody agreed to: a raw BM25 or cosine value is an implementation detail of *this* index, not a number a client should ever come to depend on |
| **C-4** | Every request ends with a returned result set or an explicit `degraded` outcome; never a silent timeout with no event | A request that produced neither a result nor a recorded reason is not a result — it is a gap the caller has to explain to someone else |
| **C-5** | Every result in a response carries which retrieval path(s) produced it and its ranking contribution, recorded on the trace | Nothing here is a black box. A result nobody can explain the presence of is a result nobody can debug when it is wrong |
| **C-6** | An ingestion event is applied to the index **at most once** per `event_id` | Idempotency at the consumer boundary, not "the producer promises no duplicates". A price-change event applied twice is a corrupted read model, silently |
| **C-7** | Free-text content in a listing's own fields never alters its ranking score | The structural defence against ranking manipulation through listing content — "click here, best offer, ranked #1" is data to index, never an instruction to a scoring stage |

**Two-assertion rule for every exclusion.** A scenario proving a listing is
excluded asserts both **the absence from the response** (`result_excludes`)
**and** the absence from either retrieval path's candidate set
(`candidate_set_excludes`). One without the other is half a test: a ranker
that filters on the way out but still asked the index for forbidden data
passes the first assertion and hides the second problem completely. This is
[`E2E-ACCEPTANCE-TESTING.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/E2E-ACCEPTANCE-TESTING.md)
§2's rule, transferred without modification from denial scenarios to
exclusion scenarios.

**Where enforcement lives, and where it deliberately does not.** C-1 and C-2
are enforced exactly once, at filter construction (`FilterResolverStage` and
each retrieval stage's independent `SearchIndexFilter` build — [§2.2](#22-the-filter-and-why-two-retrieval-paths-build-it-independently)).
That is a deliberate departure from the layered-enforcement split
[`PAYMENTS-AND-MONETIZATION.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/PAYMENTS-AND-MONETIZATION.md)
§7 states for a payment write: a second layer that silently re-filtered
every candidate on the way out would make [§8.6](#86-proving-the-suite-can-fail)'s
`skip-delisted-check-on-vector-path` mutation unfalsifiable — the bug would
exist in the code and be unobservable in any trace, which is worse than no
test at all. Only C-3 (never leak an internal identifier) gets a second,
independent layer, at `ResponseAssemblerStage` — because no planned mutation
targets it, defending it twice costs nothing and there is no test it could
make meaningless.

The exclusion and adversarial scenarios still assert **both** halves of C-1
and C-2 — that the pipeline never asked a retrieval path for the forbidden
candidate, and that nothing would have reached the response if it had — but
both halves are assertions about the *same* enforcement point (the filter each
stage built), read from two different places in the trace: the `filter.rejected`
events and the `search_index.result_ids` tag on that stage's span. This is
weaker than two independent production-code layers and is recorded as such
rather than implied by the vocabulary alone.

## 5. Success criteria and rubric anchors

**Graded by Layer 2**, thresholded and trended — never hard-blocking at 100%
the way constraints do. The judge sees the ranked trace, not just the top-k
listing titles, or it grades fluency of a title and calls it relevance.

Each criterion is scored on a small ordinal scale with an anchor per level.
"Rate this ranking 1–10" produces a number with no meaning to regress
against, so it does not appear here.

### `relevance` (0–3) — threshold ≥ 2.5 mean, no single score below 2

| Score | Anchor |
|---|---|
| 3 | The top result is the best match in the allowed candidate set for the query's intent; the rest of the top-5 is defensible in the order shown |
| 2 | The top-5 is all reasonable matches, but the best match is not first |
| 1 | The top-5 contains at least one listing with no plausible connection to the query |
| 0 | The top result contradicts an explicit hard filter's *intent* even though it technically satisfies it (e.g. a studio returned first for "family apartment, 4+ rooms" because 4 was a soft reading of a filter that should have been hard) |

### `attribution-clarity` (0–3) — threshold ≥ 2.5 mean

| Score | Anchor |
|---|---|
| 3 | For every top-5 result, the source (lexical / vector / both) and the dominant contributing signal are recoverable from the trace without guessing |
| 2 | Source is recoverable for every result, but the dominant signal requires inference |
| 1 | Source is missing or ambiguous for at least one top-5 result |
| 0 | A result's source cannot be determined from the trace at all |

### `exclusion-honesty` (0–3) — threshold ≥ 2.5 mean, applies to the exclusion class

| Score | Anchor |
|---|---|
| 3 | Every excluded listing that a naive text match would have surfaced is verifiably absent from both the response and the candidate trace |
| 2 | Absent from the response, but the candidate trace shows the exclusion happened later than it should have (e.g. after retrieval rather than before) |
| 1 | Absent from the response only because of a coincidental low score, not a structural exclusion |
| 0 | Present |

### `degradation-honesty` (0–3) — threshold ≥ 2.5 mean, applies to the degradation class

| Score | Anchor |
|---|---|
| 3 | Names which stage failed, states exactly which candidates are therefore missing, and returns the rest without pretending the ranking is complete |
| 2 | Says the outcome is degraded and which stage failed, but not what is therefore missing |
| 1 | Vague — degraded outcome with no stage named |
| 0 | Presents a partial result set as a complete one — the failure mode this criterion exists for |

**Calibration governs whether these scores may gate anything.** Until
agreement is recorded, judge scores are reported and trended but do not
block — stated here so the gap is a decision rather than drift
([D-1](DEVIATIONS.md)).

## 6. Out of scope

Stated as explicit exclusions with specified behaviour, because "the service
does not do X" without saying what it does *instead* is an untested path.

| # | Out of scope | Expected behaviour | Scenario |
|---|---|---|---|
| **O-1** | Writing to the index over HTTP | No such route exists. The only write path is `IngestionConsumer` | `NoHttpRouteReachesTheIndexTests` (`ListingSearch.Service.Tests`) |
| **O-2** | A draft listing appearing to anyone but its owner | Draft listings are excluded from `AllowedStatuses` for every unauthenticated query; owner-scoped preview is out of scope for this POC | `exc-004` |
| **O-3** | Personalised ranking (user history, saved searches) | Every query is scored identically regardless of who asks; no per-user signal exists in the pipeline | *(design-level; unasserted — no personalisation code path exists to test)* |
| **O-4** | Spelling correction / fuzzy query rewriting | A misspelled query is scored on the tokens as given; no query-rewrite stage exists | *(design-level; unasserted — see [D-8](DEVIATIONS.md))* |
| **O-5** | Multi-tenant catalogues or authentication beyond the single fictional owner directory | One fictional catalogue, visible identically to every caller | — |

## 7. Degradation contract

When a retrieval path times out or the index reports unhealthy shards, the
pipeline degrades **per stage**. The rules, restated as testable properties:

1. **Partial output with an explicit note.** What succeeded is used; what
   failed is named. `degradation.noted` carries the stage that failed.
1. **Never a fabricated result.** A failed vector path does not fall back to
   returning lexical results relabelled as hybrid. A result's `source`
   attribution is always the truth of which path actually produced it.
1. **Never a silent retry loop.** At most two attempts per **read** call per
   request (`call_attempts`), after which the pipeline stops and reports
   degradation. Write calls (`IndexAsync`, `DeleteAsync` inside
   `IngestionConsumer`) get exactly one attempt — a second attempt on a write
   is not resilience, it is [C-6](#4-hard-constraints)'s failure mode.
1. **A failed retrieval path does not become an empty result.** If the vector
   path fails, the response is built from lexical candidates alone, marked
   `degraded`, not an empty list presented as "no matches".

### 7.1 A definite failure and an indeterminate one are different answers

| What happened | What the response must say | Retry? |
|---|---|---|
| Index returned a shard-unavailable error | That retrieval path's candidates are **missing**, named | Up to the attempt cap |
| Index call **timed out** | That retrieval path's candidates are **unknown** — some shards may have answered | **No.** Not once, past the injected timeout |

### 7.2 Ingestion degradation

A malformed or unparseable event is not applied and is not silently dropped:
it is logged as an ingestion failure distinct from `ingestion.applied` and
`ingestion.duplicate_ignored`, and the event's `event_id` is **not** marked
processed — so a corrected replay of the same `event_id` is not mistaken for
a duplicate. This is the one place `event_id` idempotency and failure
handling interact, and it is stated because getting it backwards
(marking a failed event "processed") would silently and permanently drop a
listing update.

## 8. How the suite runs

The mapping this repository adopts from
[`TESTING-STRATEGY.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/TESTING-STRATEGY.md)'s
three tiers, stated rather than assumed:

| Eval layer | Testing tier it behaves as | Trigger | Gate |
|---|---|---|---|
| Layer 1, constraint scenarios | Smoke | Every PR | 100%, hard block |
| Layer 1, behaviour scenarios | Smoke | Every PR | At or above recorded baseline |
| Mutation pass (4 variants) | Suite-health signal, per [`E2E-ACCEPTANCE-TESTING.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/E2E-ACCEPTANCE-TESTING.md) §2 | Every PR touching a pipeline stage or `ISearchIndex`; periodically otherwise | Every variant must be caught; a survivor is a missing scenario |
| Layer 2, full set | Extended | Nightly / on demand (key present) | Threshold + trend |

### 8.1 Budgets

| | Budget | On breach |
|---|---|---|
| Layer 1, whole corpus | **≤ 2 minutes** on a PR | Pruned, not renamed |
| Mutation pass | **≤ 1 minute** on a PR | Pruned, not renamed |
| Layer 2, full set | ≤ 10 minutes, ≤ $0.50 | Subset shrinks |

### 8.2 Determinism, and what "100%" quantifies over

- **The gated path is deterministic by construction.** The lexical scorer is
  a pure term-overlap function; the vector embedding is a pure deterministic
  hashing function of the listing text (never a live model call — [D-1](DEVIATIONS.md)
  covers what that does and does not prove). Layer 1 has no sampling to do.
  **n = 1**, and a failure is a failure.
- **What that path does and does not grade, stated plainly.** A green Layer 1
  run means the pipeline's structure works — filter resolution, the
  candidate boundary, attribution, termination, no internal-field leakage —
  and it does **not** mean the toy embedding is a good embedding. That is
  explicitly out of scope for this suite to claim ([D-1](DEVIATIONS.md),
  [D-2](DEVIATIONS.md)).
- **A failed scenario is never re-run to green.** There is no retry setting
  in the harness.
- **"100% pass" quantifies over constraint scenarios on that single run**,
  not over samples — there are no samples on the gated path.

### 8.3 Fixture isolation

Every scenario reconstructs its world from scratch: a fresh in-memory index,
a fresh idempotency registry, seeded from the named fixture plus the
scenario's own ingestion steps. Nothing survives between scenarios.

### 8.4 What a fixture edit costs

A baseline records a pass rate against a specific corpus. Editing
`evals/fixtures/*.yaml` changes what the baseline measured without changing a
single scenario file. Therefore: **a fixture edit is a suite version bump and
forces a re-baseline**, reviewed in the same pull request.

### 8.5 Two kinds of skip, reported separately

| Marker | Meaning | Legitimate? |
|---|---|---|
| `skipped:unimplemented` | The scenario exists, the capability does not yet | Yes, with a reason and a date |
| `skipped:no-credential` | Layer 2 had no judge key | Yes, on a PR — **not** as the only outcome that ever occurs |

### 8.6 Proving the suite can fail

> *"Once a test has a real assertion, that only proves it can pass — not that
> it can catch anything."* — [`E2E-ACCEPTANCE-TESTING.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/E2E-ACCEPTANCE-TESTING.md) §2

Four deliberately broken pipeline variants, each swapping exactly one stage,
with no flag and no log announcing the mutation — see
[§5 of the design note](../README.md#the-mutation-pass) and
[`docs/FINDINGS.md`](FINDINGS.md) for what each run actually caught:

- `disable-hard-price-filter` — breaks C-2.
- `skip-delisted-check-on-vector-path` — breaks C-1, on the vector path only.
- `apply-event-twice-on-retry` — breaks C-6.
- `rerank-boosts-flagged-text` — breaks C-7.

A variant that survives is a missing scenario, not a curiosity.

## 9. Assumptions

Written down because an assumption nobody stated is a defect nobody can find.

- **Single fictional catalogue, one owner directory, no authentication.** See
  [§6](#6-out-of-scope), O-5.
- **The clock is not load-bearing.** Unlike a booking agent, ranking a search
  query has no relative-date arithmetic; `ListedAt` is a fixture value, never
  compared to a live clock during a request.
- **The embedding is a deterministic hash, not a trained model.** [D-1](DEVIATIONS.md)
  states plainly what this does and does not prove about ranking quality.
- **The lexical scorer is term-overlap, not full BM25.** Close enough to
  exercise every structural property this specification tests, and named
  as a simplification rather than presented as production-grade relevance
  tuning.
- **The corpus is on the order of tens of listings, not production scale.**
  [D-2](DEVIATIONS.md).
- **No spelling correction or query rewriting.** [D-8](DEVIATIONS.md) and
  [§6](#6-out-of-scope) O-4.

## 10. How this document changes

1. A behaviour change starts here, not in a ranking-weight config.
1. The change lands with its scenarios in the same pull request.
1. The version at the top is bumped.
1. The baseline is re-recorded and the diff is reviewed as part of the change.

A pull request that changes a pipeline stage or `ISearchIndex` without
touching this document is a pull request whose behaviour change nobody wrote
down.
