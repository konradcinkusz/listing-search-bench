# Findings — what the suite actually caught

Numbers first, recomputed from the corpus itself rather than copied and left to go
stale. Every count below is reproducible: `dotnet test --project evals/Homefinder.Evals`.

## 1. The suite, in numbers

| | Count |
|---|---|
| Scenarios | 28 |
| Assertions | 128 |
| Mean assertions per scenario | 4.6 |
| Constraint-gated scenarios (100% required) | 13 |
| Absence assertions (`result_excludes`, `candidate_set_excludes`, `event_not_emitted`) | 28 (22%) |
| Mutation variants | 4 |
| Unit tests (`Homefinder.SearchService.Tests`) | 53 |

| Class | Scenarios | Assertions | Mean |
|---|---|---|---|
| happy | 7 | 36 | 5.1 |
| ambiguity | 5 | 20 | 4.0 |
| exclusion | 5 | 21 | 4.2 |
| adversarial | 4 | 18 | 4.5 |
| degradation | 7 | 33 | 4.7 |

| Assertion type | Occurrences |
|---|---|
| `result_includes` / `result_excludes` | 40 |
| `candidate_set_includes` / `candidate_set_excludes` | 18 |
| `outcome` | 25 |
| `event_emitted` / `event_not_emitted` | 13 |
| `result_rank` / `result_ranked_below` | 9 |
| `result_attribution` | 5 |
| `ingestion_outcome` | 13 |
| `call_attempts` | 2 |
| `result_count` | 2 |
| `response_excludes_internal_fields` | 1 |

## 2. What it caught

| # | Defect | Found by | Severity |
|---|---|---|---|
| F-1 | Two scenario assertions expected an exact rank 1/2 for the only two candidates a query "should" have matched, on the assumption that only those two would score above zero | Running the harness against the real pipeline (`hap-002`) | Medium — a wrong test, not a wrong pipeline |
| F-2 | An earlier design for C-1/C-2 enforcement put a second, independent status/price check inside `InstrumentedSearchIndex`, re-validating every hit against the filter it was given | Reasoning through what the mutation pass would need to prove, before any mutation code existed | High — would have made the two hard-constraint mutations structurally unfalsifiable |

### F-1: a scenario assumed a candidate set it never measured

`hap-002` asserted `lst-1012` at rank 1 and `lst-1002` at rank 2 for `query: studio,
city: Zurich, sort: price_ascending`, on the reasoning that those were the only two
listings whose title or description contains "studio". The deterministic embedding
(a feature-hashed bag of words, `Pipeline/Embedding/DeterministicTextEmbedding.cs`)
does not know what "studio" means — it hashes tokens into a fixed number of buckets,
and two unrelated words can land in the same bucket by coincidence. Two further
Zurich listings scored a nonzero cosine similarity against "studio" through exactly
that coincidence, entered the candidate set, and (being cheaper) sorted ahead of both
expected results under `price_ascending`.

The fix was not in the pipeline — the pipeline's behaviour was correct by its own
contract, a hash-collision candidate is a legitimate low-relevance match, not a bug.
The fix was the assertion: `result_rank ... value: '1'/'2'` (exact position, which
implicitly asserts "and nothing else scored") became `result_ranked_below: {listing:
lst-1002, than: lst-1012}` (a relative ordering claim, which does not). This is
recorded because it is exactly the failure mode a hand-verified test corpus is prone
to and a "confirmed against the real system" one catches: an assumption about what
*isn't* a candidate, asserted without ever having enumerated the candidate set.

### F-2: a design that would have defended against nothing

The first draft of the layered-enforcement story for C-1 and C-2 (mirroring
`agent-eval-bench`'s tool-boundary pattern almost by reflex) added a second check at
`InstrumentedSearchIndex`: re-validate every hit against the filter before returning
it, independently of what the retrieval stage decided. Working through what
`skip-delisted-check-on-vector-path` (SPEC §8.6) would actually need to prove showed
the problem: if the decorator re-checks hits against *the same filter object* the
stage constructed, a stage that builds a wrong filter is still caught by the
decorator — which means the mutation could never leak anything, which means the test
that is supposed to prove the scenario catches the mutation would pass regardless of
whether the scenario's assertions do anything at all. A defence that cannot be
defeated by the bug it is supposed to catch is not a defence; it is a reason the
mutation pass would read green for the wrong reason. `docs/SPEC.md` §4 ("Where
enforcement lives, and where it deliberately does not") records the corrected design
and the reasoning, in place rather than as a footnote.

## 3. Where the findings actually came from

| Source | Findings |
|---|---|
| Writing the scenario corpus against the real pipeline | F-1 |
| Reasoning through the mutation pass before writing it | F-2 |
| The pipeline behaving unexpectedly in production | — (no production traffic exists; see [D-5](DEVIATIONS.md)) |

Not one defect on this list was found by the suite passing or failing on the search
pipeline itself — both were found by building the instrument. That is the expected
shape for a specification-first repository: the pipeline was written against
`docs/SPEC.md`, so most of what a first pass finds is friction in the measuring
apparatus, not in the thing being measured.

## 4. The mutation pass

All four variants were caught on the first run — every one of the twelve
constraint-gated scenarios still passes with the real pipeline, and each targeted
scenario fails once its one stage or consumer is swapped for a broken variant. Full
detail: `evals/Homefinder.Evals/Mutations/BrokenStages.cs`; the wiring that swaps a
DI registration for its broken twin is `Mutations/BrokenPipeline.cs`.

| Variant | Breaks | Caught by | Result |
|---|---|---|---|
| `disable-hard-price-filter` | C-2 | `exc-005-high-score-never-buys-a-way-past-a-price-ceiling` | **Caught** |
| `skip-delisted-check-on-vector-path` | C-1, vector path only | `exc-001-delisted-never-appears-despite-top-lexical-score` | **Caught** |
| `apply-event-twice-on-retry` | C-6 | `adv-002-replayed-publish-event-does-not-resurrect-a-delisting` | **Caught** |
| `rerank-boosts-flagged-text` | C-7 | `adv-001-ranking-manipulation-in-listing-text-is-ignored` | **Caught** |

**A caveat stated plainly rather than left implicit.** In `agent-eval-bench`, one of
four mutants survived its first run (F-1 in that repository) — direct evidence the
pass was doing real work rather than confirming what its own author already expected.
Here, all four were caught immediately. That is a weaker result than it looks: the
scenarios and the mutations were written by the same hand, in the same sitting, with
full knowledge of how each mutation would break the pipeline — `exc-001` was written
*because* `skip-delisted-check-on-vector-path` needed a scenario that would catch it,
not the other way round. A mutation pass is strongest as evidence when the scenarios
predate knowledge of the mutations, or when a second person writes one half. Neither
is true yet here, and the honest reading is: **this proves the assertions are
internally consistent with the design intent, not that they would catch a bug nobody
had in mind while writing them.** Closing that gap is [D-7](DEVIATIONS.md).

## 5. What it has not caught, and cannot claim

- **No live model anywhere in this repository.** Neither the vector path
  (`DeterministicTextEmbedding`, a feature-hashed bag of words) nor the Layer 2
  judge (every rubric built and versioned, none ever scored) has ever involved a
  trained or live model. Every claim in this document about "the vector path
  recalling a genuine match" ([`amb-002`](../evals/scenarios/ambiguity/amb-002-vector-path-recalls-what-lexical-misses.yaml))
  is a claim about a deterministic hash function, not about semantic search quality
  generally — [D-1](DEVIATIONS.md).
- **No corpus at production scale.** 26 scenarios run against a ~20-listing fixture.
  Nothing here has been measured against the tens or hundreds of thousands of
  listings a real deployment would carry — [D-2](DEVIATIONS.md).
- **No real ingestion traffic.** Every ingestion event in this repository was
  authored by a scenario file and replayed through the eval harness; no real queue
  has ever been wired up or run — [D-3](DEVIATIONS.md).
- **No live Elasticsearch cluster.** `ElasticsearchIndex.cs` is written from the
  client SDK's own documented shapes and has never been run against a real cluster,
  single-node or otherwise — [D-4](DEVIATIONS.md).
- **No production loop.** No scenario carries `origin.kind: production-trace`,
  because no deployed, trafficked endpoint exists to extract an incident from —
  [D-5](DEVIATIONS.md).
- **The C-7 defence has only been attacked by the scenarios planned for it.** Two
  adversarial scenarios, never a wider, adversarially-generated corpus trying new
  phrasings of the same attack — [D-6](DEVIATIONS.md).
- **No independently authored mutation pass.** §4's caveat, restated: the same person
  wrote the scenarios and the mutants — [D-7](DEVIATIONS.md).
- **No spelling correction or query rewriting.** A misspelled query is scored on the
  tokens as typed — [D-8](DEVIATIONS.md).

This section is expected to still say roughly this on the day the repository is
read, unless one of the linked deviations has since closed.
