# listing-search-bench — Comprehensive Project Documentation

**A spec-first evaluation bench for a hybrid search and ranking service.**

This document is a single, linear read — start to end, no badges, no
collapsible sections — for a reader who wants the whole shape of this
repository in one sitting, or who wants to attach one file to something. The
[`README`](../README.md) is the right length for someone deciding whether to
keep reading; this is the document for after they decide to.

Source of truth for every number and every claim below: this repository's own
`docs/SPEC.md`, `docs/FINDINGS.md`, and `docs/DEVIATIONS.md`. This document
introduces no fact those do not already contain — it exists to narrate them in
order, not to restate them independently and risk drifting from them.

**Rendering.** This file is also the source for an on-demand PDF, built by
[`.github/workflows/build-overview-pdf.yml`](../.github/workflows/build-overview-pdf.yml)
(`workflow_dispatch` only — [§16](#16-getting-the-pdf)). The PDF is never
committed to this repository.

## Contents

1. [Executive summary](#1-executive-summary)
1. [Why this exists](#2-why-this-exists)
1. [The specimen: a hybrid search service](#3-the-specimen-a-hybrid-search-service)
1. [Architecture](#4-architecture)
1. [The spec-first workflow](#5-the-spec-first-workflow)
1. [The two-layer evaluation methodology](#6-the-two-layer-evaluation-methodology)
1. [What the evals actually caught](#7-what-the-evals-actually-caught)
1. [CI and governance](#8-ci-and-governance)
1. [Production readiness, honestly](#9-production-readiness-honestly)
1. [Deviations from the standards](#10-deviations-from-the-standards)
1. [Architecture decision records](#11-architecture-decision-records)
1. [Values and engineering philosophy](#12-values-and-engineering-philosophy)
1. [Status and roadmap](#13-status-and-roadmap)
1. [Non-goals](#14-non-goals)
1. [Relationship to architecture-standards and agent-eval-bench](#15-relationship-to-architecture-standards-and-agent-eval-bench)
1. [Getting the PDF](#16-getting-the-pdf)

---

## 1. Executive summary

A user searches a catalogue of real-estate listings — free text plus
structured filters (price, city, rooms) — and `ListingSearch.Service`
returns a ranked result set built by combining two independent retrieval
paths: lexical (BM25-style term overlap) and dense-vector similarity. Full-text
search over a document store is solved. What this repository is actually
about is the harder, less-solved half: **a ranker that combines two retrieval
paths without letting either become the hole the other path's guarantees leak
through.**

Every claim this repository makes about that ranker is backed by a
specification written before the pipeline existed
([`docs/SPEC.md`](SPEC.md)), a corpus of 28 scenarios run against the real
pipeline in-process, and a four-variant mutation pass that proves the
scenarios can actually fail. Where the repository falls short of a
production-ready service, that gap is dated, reasoned, and tracked — in
[`docs/DEVIATIONS.md`](DEVIATIONS.md) and in
[a public roadmap issue](https://github.com/konradcinkusz/listing-search-bench/issues/3)
— rather than implied by silence.

This is a proof of concept, built as independent, public proof of methodology
on a synthetic corpus — not a contribution to any production codebase, and not
(yet) a deployed service. [§9](#9-production-readiness-honestly) says exactly what
that does and does not mean.

## 2. Why this exists

### 2.1 The trigger is not "a ranking change"

A ranking-weight tune, a filter refactor, or a change to how a listing's own
text is scored ships, in most codebases, the way a config diff ships —
casually, with no reviewer positioned to ask "does this still respect every
hard constraint the business places on ranking?" A test suite that only
checks whether the service returns 200 OK cannot answer that question. This
repository exists to make the question checkable, mechanically, on every
change.

### 2.2 What an eval actually is, here

Not a prompt-quality score. An **eval**, in this repository, is a scenario —
a request or an ingestion event, played through the real pipeline — plus a
set of assertions over the resulting **trace**: which candidates each
retrieval path returned, how they were filtered, what was ranked where, and
— just as importantly — what was **never** a candidate at all. 27 of this
repository's 116 Layer 1 assertions (23%) assert exactly that kind of
absence.

### 2.3 Why a hybrid retrieval system specifically

A single-path search engine cannot have this repository's central failure
mode. A hybrid system can: two independent code paths that must agree on
every hard constraint, where a bug in one and not the other is invisible
unless something specifically goes looking on both paths. `docs/SPEC.md` §2.2
states, and [§4](#4-architecture) below shows, why this repository's filter is
built independently by each retrieval path rather than shared as one mutable
object — that independence is the exact seam an asymmetric bug lives in, and
sharing the object would paper over it rather than test it.

## 3. The specimen: a hybrid search service

`POST /search` takes free text plus structured filters against a synthetic
Swiss real-estate catalogue in French, German and English. The ranker
combines:

- **Lexical retrieval** — BM25-style term overlap against title and
  description (`LexicalRetrieverStage`, `ISearchIndex.QueryAsync`).
- **Vector retrieval** — cosine similarity against a text embedding
  (`VectorRetrieverStage`, `ISearchIndex.VectorQueryAsync`). The embedding
  itself is a deterministic, feature-hashed stand-in for a trained model
  (`DeterministicTextEmbedding`) — [§9](#9-production-readiness-honestly) and
  [§10](#10-deviations-from-the-standards) state plainly what that does and
  does not prove.

`HybridRankerStage` merges both candidate sets, attributes every result to
`lexical`, `vector`, or `both`, and — the part with an adversary in mind —
scans a listing's own free text for ranking-manipulative language
(`RankingManipulationScanner`), reports what it finds, and **never** lets
that finding move a score. The only two numbers that ever feed
`RankedCandidate.CombinedScore` are a lexical match value and a cosine
similarity, computed the same deterministic way for every listing in the
corpus.

The only write path is `IngestionConsumer`, reading `listing.published`,
`listing.price_changed`, and `listing.delisted` events off a queue,
idempotent by `event_id`. There is deliberately no HTTP route that writes to
the index — a convenience endpoint would hand every adversarial scenario a
way around the exact thing this repository tests.

## 4. Architecture

### 4.1 The request pipeline

```text
POST /search
  → QueryParserStage        tokenises the query
  → FilterResolverStage     resolves hard filters once; AllowedStatuses = [Active], always
  → LexicalRetrieverStage   builds its OWN filter, independently — QueryAsync
  → VectorRetrieverStage    builds its OWN filter, independently — VectorQueryAsync
  → HybridRankerStage       merges, attributes, scans for manipulation (never scores it)
  → ResponseAssemblerStage  strips any internal id, raw score, or embedding vector
  → SearchResponse           completed | degraded, never a silent failure
```

`ISearchIndex` is the one interface either retrieval stage reaches a backend
through — five methods (`QueryAsync`, `VectorQueryAsync`, `IndexAsync`,
`DeleteAsync`, `HealthAsync`), normalised so nothing above this seam ever
names a vendor. `InMemoryFixtureIndex` is the default, zero-credential
implementation every scenario and every CI run uses; `ElasticsearchIndex` is
the real backend, written from the client SDK's own documented shapes,
dev-only by construction (§9).

### 4.2 The embedding seam

`IEmbeddingProvider` sits between the pipeline and any embedding computation
— `VectorRetrieverStage` and `ElasticsearchIndex.IndexAsync` call it, never a
static hashing function directly. The default implementation
(`DeterministicEmbeddingProvider`) wraps the same deterministic hash the
pipeline always used, so today's behaviour is unchanged; the seam exists so a
trained model can be substituted later without touching either caller.

### 4.3 Ingestion, idempotency, and reordering tolerance

`IngestionConsumer` reserves every event's `event_id` before applying
anything (`IEventIdempotencyStore`); a reservation that fails means the event
was already applied, and it is reported `ingestion.duplicate_ignored`, not
re-applied. A `price_changed` or `delisted` event that names a listing this
consumer has never seen a `published` event for is **deferred**, not
rejected — buffered per listing (`IPendingEventBuffer`) and replayed
automatically once that listing's `published` event arrives, because a real
transport does not guarantee delivery order across partitions. A listing
whose `published` event never arrives before its pending buffer fills is
**dead-lettered** (`IDeadLetterSink`) rather than buffered forever.

### 4.4 Instrumentation

Every stage and every index call is an OpenTelemetry span
(`search_stage {name}`, `search_index {operation}`); every constraint-relevant
decision is a span event on top of that (`filter.rejected`,
`ranking.manipulation_ignored`, `ingestion.applied`, `ingestion.deferred`,
`ingestion.dead_lettered`, `degradation.noted`, and others —
`docs/SPEC.md` §2.3's full table). This trace is the interface between the
pipeline and its evals: Layer 1 reads it, on every push, needing no model, no
network, and no credential.

## 5. The spec-first workflow

`docs/SPEC.md` existed before a single line of `ListingSearch.Service`
did. It defines the vocabulary the eval harness and this document both use —
what a "candidate" is, what "degraded" means, which trace events are
contract rather than diagnostics — twelve expected behaviours (B-1 through
B-12), seven hard constraints (C-1 through C-7), and five judge rubrics.

The rule this repository holds itself to: **a behaviour change starts in
`docs/SPEC.md`, lands with its scenarios in the same pull request, and bumps
the version at the top.** A pull request that changes a pipeline stage or
`ISearchIndex` without touching that document is a pull request whose
behaviour change nobody wrote down. Spec version 1.1.0 (this document's
current source) added B-12 — the deferred-and-replayed ingestion behaviour
in [§4.3](#43-ingestion-idempotency-and-reordering-tolerance) — in exactly
that shape: spec change, code change, and two new scenarios
(`deg-006`, `deg-007`) in one pull request.

## 6. The two-layer evaluation methodology

### 6.1 Layer 1 — deterministic trace assertions

116 assertions across 28 YAML scenarios in five classes (happy, ambiguity,
exclusion, adversarial, degradation), run against the real pipeline
in-process by `ScenarioRunner`. No model, no network, no credential — the
gated path is deterministic by construction (`docs/SPEC.md` §8.2), so a
failure is a failure, not a sample that might pass on retry. Constraint-gated
scenarios (12 of them) hard-block at 100%; behaviour scenarios are measured
against a recorded baseline (`evals/baselines/layer1.json`), and a regression
is a fact, not a vibe.

### 6.2 Layer 2 — a rubric-anchored judge

Five rubrics (`relevance`, `attribution-clarity`, `exclusion-honesty`,
`degradation-honesty`), each a small ordinal scale with a written anchor per
level — "rate this ranking 1–10" produces a number with no meaning to regress
against, so it does not appear anywhere in this repository. The judge's
prompt and rubrics are hashed at load time so an edit to either is a version
bump whether or not a human remembered to move the number. On a
credential-less run — every CI run today — it reports
`skipped:no-credential`, never a silent green.

### 6.3 Proving the suite can fail

Four deliberately broken pipeline variants, each swapping exactly one
registration — a stage or the ingestion consumer — for a broken twin that
keeps the original's name and changes exactly one behaviour:
`disable-hard-price-filter`, `skip-delisted-check-on-vector-path`,
`apply-event-twice-on-retry`, `rerank-boosts-flagged-text`. Every one is
caught by a specific, named scenario. [§7](#7-what-the-evals-actually-caught)
states the honest caveat about what that catching does and does not prove.

## 7. What the evals actually caught

Two findings to date, both from building the measuring instrument, neither
from the pipeline behaving unexpectedly under real traffic — there has never
been any:

- **F-1.** A scenario asserted exact ranks on the unstated assumption that
  only two candidates would score above zero for a given query. Running the
  harness against the real pipeline surfaced a third candidate, a hash
  collision in the deterministic embedding. The fix was the assertion — from
  an exact-rank claim to a relative-order claim — not the pipeline, which was
  correct by its own contract.
- **F-2.** An early design for enforcing C-1 and C-2 would have put a second,
  independent filter check inside the index decorator. Reasoning through what
  the mutation pass would need to prove — before any mutation code existed —
  showed this would make the intended defect structurally unfalsifiable: a
  stage that built a wrong filter would still be caught by the decorator
  re-checking the same filter object, so the mutation could never leak
  anything, and the test proving the scenario catches the mutation would pass
  for the wrong reason.

**The honest caveat on the mutation pass itself:** the same person wrote the
scenario corpus and the four broken variants, in the same sitting, with full
knowledge of how each mutation would break the pipeline. All four were caught
on the first run — which is weaker evidence than it looks. It proves the
assertions are internally consistent with the design intent, not that they
would catch a bug nobody had in mind while writing them. Closing that gap
(D-7) needs a second, independent author.

Full detail, including what the suite has *not* caught and cannot claim:
[`docs/FINDINGS.md`](FINDINGS.md).

## 8. CI and governance

| Workflow | Trigger | What it does |
|---|---|---|
| `ci.yml` | Every push and pull request | `lint-docs` (markdown + Ajv scenario/fixture schema validation), `build-test` (unit tests, Layer 1, the mutation pass, judge machinery), `secret-scan` (gitleaks) — zero credentials required |
| `nightly.yml` | Scheduled, keyed | The full Layer 2 judged set. Reports `NOT RUN` without a configured `LLM_API_KEY`, honestly, rather than a silent skip nobody notices |
| `build-overview-pdf.yml` | `workflow_dispatch` only | Renders this document to PDF ([§16](#16-getting-the-pdf)) |

There is deliberately no deploy workflow. [§9](#9-production-readiness-honestly)
explains why, and what would need to exist first.

## 9. Production readiness, honestly

This is a proof of concept, not a deployed service, and this section says
exactly what that means rather than leaving a reader to infer it from an
absent demo link.

**What is real:** the pipeline, the eval harness, the mutation pass, and the
CI that runs all three on every push, with zero credentials configured.

**What is not, yet** — each dated and reasoned in
[`docs/DEVIATIONS.md`](DEVIATIONS.md):

- No live embedding model or LLM judge has ever run (D-1) — the vector path
  is a deterministic hash, and the judge has never scored a real response.
- No corpus at production scale (D-2) — 28 scenarios against a ~20-listing
  fixture.
- No real ingestion traffic (D-3) — every event was authored by a scenario
  file and replayed through the harness.
- No live Elasticsearch cluster, ever (D-4) — `ElasticsearchIndex.cs` is
  written from the SDK's documented shapes, never run against a real
  cluster.
- No production loop (D-5) — no deployed, trafficked endpoint exists to
  extract an incident from, and no deploy workflow exists at all, by
  deliberate non-goal.
- The C-7 ranking-manipulation defence has only been attacked by the two
  scenarios planned for it (D-6), not a wider adversarial corpus.
- The mutation pass was not independently authored (D-7) — [§7](#7-what-the-evals-actually-caught)'s
  caveat.
- No spelling correction or query rewriting exists (D-8).

A [public roadmap issue](https://github.com/konradcinkusz/listing-search-bench/issues/3)
tracks exactly what closing each of these — plus what a genuinely
production-ready deployment needs beyond them (authentication, a deploy
pipeline, load testing at scale, real observability) — would take, organised
into ordered workstreams rather than a flat wishlist. Three self-contained
pieces of it have already landed without needing any external
infrastructure: rate limiting and a bounded page size on `POST /search`, the
`ISearchIndex.HealthAsync()` readiness-probe wiring, and the ingestion
reordering-and-dead-letter behaviour in [§4.3](#43-ingestion-idempotency-and-reordering-tolerance).

## 10. Deviations from the standards

[`docs/DEVIATIONS.md`](DEVIATIONS.md) is the authoritative, dated list — open
from the first commit, not written retroactively. Beyond the eight rows
[§9](#9-production-readiness-honestly) summarises, it also records two
extensions this repository proposes back to the standards it follows (a
mutation-testing requirement for eval suites generally, and a
fixture-composition pattern for eval scenarios) and three patterns the
worked example this repository mirrors carries that this repository
deliberately does not: a confirmation-gate write boundary (there is no write
path a human approves — the only write is an automated consumer), a showcase
frontend (this repository is about backend and ranking engineering, not UI), and a
tag-driven public deployment (no public demo exists for this POC).

## 11. Architecture decision records

One file per decision, numbered sequentially, never renumbered or rewritten
after acceptance — [`docs/adr/`](adr/):

| # | Title | Status |
|---|---|---|
| 0001 | Record architecture decisions | Accepted |
| 0002 | Mock index first, zero credentials by default | Accepted |
| 0003 | Assertions read the structural trace, never response text | Accepted |
| 0004 | Pin the embedding and the judge model separately, never fall back silently | Accepted |
| 0005 | The Elasticsearch and vector-store SDK lives behind a five-method seam | Accepted |
| 0006 | Event idempotency at the consumer boundary | Accepted |
| 0007 | Render this document to PDF on demand, never committed | Accepted |

## 12. Values and engineering philosophy

- **The spec is the contract; the code is measured against it, not the other
  way round.** [§5](#5-the-spec-first-workflow).
- **A claim without a measurement is marketing.** Every number in this
  document traces back to a file the eval suite itself produces or a
  document version-controlled alongside the code it describes.
- **Absence is worth proving, not just presence.** 27 of 116 Layer 1
  assertions (23%) assert that something never happened — never a candidate,
  never in a response, never re-applied on replay.
- **A test suite that has never failed is a suite nobody has tested.** The
  mutation pass exists because "the constraint scenarios pass" and "the
  constraint scenarios would catch a violation" are different claims, and
  only one of them is provable by watching green CI.
- **An honest gap, dated and reasoned, is a decision. An unstated one is
  drift.** `docs/DEVIATIONS.md` exists because "we know this isn't
  production-ready" is not the same sentence as writing down which specific
  parts aren't, and why, and what would close each one.

## 13. Status and roadmap

Complete, tested, and honestly documented as a proof of concept: the full
pipeline, 116 Layer 1 assertions across 28 scenarios, the four-variant
mutation pass, and CI that runs all of it with zero credentials configured.
Not complete, and not claimed to be, as a production service — [§9](#9-production-readiness-honestly).

The forward plan lives as a
[public GitHub issue](https://github.com/konradcinkusz/listing-search-bench/issues/3)
rather than in this document, so it can be checked off in place rather than
going stale here: eight ordered workstreams, from a real Elasticsearch
backend and a real embedding model through a deploy pipeline, load testing
at scale, and closing the two eval-rigor gaps ([§7](#7-what-the-evals-actually-caught))
that need a second, independent author rather than more code.

## 14. Non-goals

Stated so that scope creep has something to fail against, mirrored from
[`README.md`](../README.md):

- No UI. A REST API and nothing else — this repository is about backend and
  ranking engineering, not frontend.
- No personalisation, no authentication beyond a single fictional owner
  directory *(tracked to close — the roadmap issue's "production-safe API
  surface" workstream)*, no multi-tenant catalogue.
- No spelling correction or query rewriting (D-8).
- No fork of `architecture-standards`. Deviations are recorded, not worked
  around.
- No real-world listing data, ever, in any fixture. Every listing, owner, and
  query in this repository is synthetic.
- No HTTP route writes to the index. The only write path is
  `IngestionConsumer`, reachable only from an ingestion event.

## 15. Relationship to architecture-standards and agent-eval-bench

This repository does not re-derive its architecture; it reads
[`architecture-standards`](https://github.com/konradcinkusz/architecture-standards)
and follows it — .NET Aspire with the AppHost as composition root, one thin
`ServiceDefaults` kernel, OpenTelemetry first — and mirrors, rather than
re-derives, the eval-bench shape
[`agent-eval-bench`](https://github.com/konradcinkusz/agent-eval-bench) —
the same author's reference implementation of the standards' AI-evaluation
guide — already demonstrates for a different specimen. The specimen changed,
from a tool-using HR agent to a hybrid ranker; the instrument's shape did
not. Where this repository must depart from either, the departure is
recorded, dated, and reasoned in [`docs/DEVIATIONS.md`](DEVIATIONS.md), not
implied by the absence of a pattern the worked example carries.

## 16. Getting the PDF

This document is rendered to PDF by
[`.github/workflows/build-overview-pdf.yml`](../.github/workflows/build-overview-pdf.yml)
via [pandoc](https://pandoc.org/)'s LaTeX backend (`pdflatex`) — manually
triggered only (`workflow_dispatch`), because this is a presentation of a
living document, not a versioned release artifact: there is no tag, no
changelog entry, and nobody depends on a specific build of it existing
anywhere. Nothing the workflow produces is committed to this repository — a
generated binary in git is a merge conflict waiting to happen and a diff
nobody can review (ADR-0007).

To get a copy: open the **Actions** tab on GitHub, select **Build Overview
PDF**, click **Run workflow**, then download
**ListingSearchBench_Overview_PDF** from the finished run's Artifacts
section. Locally, with pandoc and a LaTeX distribution installed:

```bash
pandoc docs/OVERVIEW.md -o ListingSearchBench_Overview.pdf \
  --from=gfm --toc --toc-depth=2 \
  -V geometry:margin=2.2cm -V colorlinks=true -V linkcolor=blue \
  --pdf-engine=pdflatex
```
