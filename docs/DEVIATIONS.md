# Deviations from the standards, and from the worked example this repository mirrors

Open from the first commit, not written retroactively once someone noticed. When one
is fixed, delete the row. When a new one is accepted deliberately, add it with the
reasoning — an acknowledged deviation is a decision; an unacknowledged one is drift.

## Open deviations

| # | Deviation | Principle / guide | Since | Reason | Closes when |
|---|---|---|---|---|---|
| D-1 | Neither the vector path's embedding nor the Layer 2 judge has ever involved a live or trained model | AI-EVALS.md §5, SPEC §5, §8.2 | 2026-08-17 | `DeterministicTextEmbedding` is a feature-hashed bag of words, not a trained representation; no `LLM_API_KEY` ships with a public repository, so every judged scenario reports `skipped:no-credential`. Everything around both — the pipeline structure, the rubric machinery, the parsing — does run on every push. An `IEmbeddingProvider` seam now sits between the pipeline and every embedding computation (`VectorRetrieverStage`, `ElasticsearchIndex.IndexAsync`), so a real model can be substituted without touching either caller — but the only implementation behind it today is still the same deterministic hash | The first run with a real embedding model behind `IEmbeddingProvider`, and separately the first keyed run with `LLM_API_KEY`/`LLM_JUDGE_MODEL`/`LLM_ENDPOINT` set |
| D-2 | The vector and lexical layers have never been tested against a catalogue at production scale | TESTING-STRATEGY.md §2 | 2026-08-17 | The corpus is ~20 listings; a production-scale index is on the order of hundreds of thousands. Nothing in this repository's numbers claims to say anything about ranking quality, latency, or memory behaviour at that scale | A load test against an index of comparable scale |
| D-3 | The ingestion pipeline has never received real event traffic | AI-EVALS.md §7 (production scoring) | 2026-08-17 | Every `listing.published` / `listing.price_changed` / `listing.delisted` event in this repository was authored by a scenario file and replayed through the eval harness (`IIngestionConsumer.ConsumeAsync`, called directly). No message queue has ever been stood up, and no real producer has ever published to one. `IngestionConsumer` now tolerates out-of-order delivery — deferring a `price_changed`/`delisted` event that names a not-yet-seen listing and replaying it once the matching `published` event arrives, dead-lettering it if that never happens (SPEC §7.2, B-12) — but this has only ever been exercised by scenario replay against an in-memory buffer, never a real transport's actual reordering | The first day this service is connected to a real event source |
| D-4 | No Elasticsearch cluster, single-node or otherwise, has ever answered a query from this repository | ADR-0002, ADR-0005 | 2026-08-17 | `ElasticsearchIndex.cs` is written from the client SDK's own documented shapes, the same honesty `agent-eval-bench` states for its MCP adapter. Degradation (`FaultInjectingSearchIndex`) is simulated entirely in-memory, never observed on a real cluster with real shard topology | A keyed CI job with a disposable Elasticsearch cluster, closing ADR-0005's stated revisit condition |
| D-5 | No production loop exists | AI-EVALS.md §7 | 2026-08-17 | Every scenario's `origin.kind` is `designed`; none is `production-trace`, because no deployed, trafficked endpoint exists to extract an incident from. There is no scoring pipeline reading live spans, and no worst-session review of anything, because there are no sessions | A deployed instance with real traffic and a scoring pass reading its trace export |
| D-6 | The C-7 ranking-manipulation defence has only been attacked by the scenarios planned for it | SPEC C-7, §8.6 | 2026-08-17 | Two adversarial scenarios (`adv-001`, `adv-004`) exercise a fixed, small set of manipulative phrasings against `RankingManipulationScanner`'s fixed pattern list. Neither an adversarially-generated corpus nor a red-team pass with new phrasings has ever run against it, and the scanner itself is stated, in its own doc comment, to be an incomplete defence by construction | A wider adversarial corpus, generated or reviewed by someone other than the scanner's author |
| D-7 | The mutation pass was not independently authored | E2E-ACCEPTANCE-TESTING.md §2, SPEC §8.6 | 2026-08-17 | The same person wrote `evals/scenarios/` and `evals/ListingSearch.Evals/Mutations/BrokenStages.cs`, in the same sitting, with full knowledge of how each mutation would break the pipeline. All four variants were caught on the first run (`docs/FINDINGS.md` §4), which is weaker evidence than it looks — it proves internal consistency between the design intent and the assertions, not that the assertions would catch a bug nobody had in mind while writing them | A second contributor writes a fifth mutation without reading `BrokenStages.cs` first, or the existing four are re-validated by someone who did not write the corpus |
| D-8 | No spelling correction or query rewriting | SPEC O-4 | 2026-08-17 | A misspelled or oddly-phrased query is scored on the tokens exactly as typed. No query-understanding stage exists between `QueryParserStage`'s tokenisation and retrieval | A query-rewrite stage is added and specified in `docs/SPEC.md`, with its own scenario class |

## Extensions proposed back to the standards

| # | Extension | Target guide | Since | Status |
|---|---|---|---|---|
| E-1 | A mutation-testing requirement for eval suites, not just E2E suites | AI-EVALS.md §4 | 2026-08-17 | Proposed, with a worked example (`docs/FINDINGS.md` §4) |
| E-2 | A fixture-composition pattern for eval scenarios (base fixture plus a per-scenario delta, YAML-node-level override) as an explicit exception to SERVICE-API-PATTERNS.md's seed-by-slug rule | AI-EVALS.md §3 | 2026-08-17 | Proposed |

## Deviations deliberately not taken

Patterns the worked example this repository mirrors carries, that this repository
does not copy, stated so the absence reads as a decision rather than an oversight.

| Not inherited | Principle | Why it matters here |
|---|---|---|
| A confirmation-gate / human-in-the-loop write boundary | AI-EVALS.md §8 | This service has no write path a human approves — the only write is an automated ingestion consumer reacting to an upstream event. There is nothing here for a human to confirm before it happens |
| A showcase frontend | REPO-BASELINE.md | This repository is about backend and search-ranking engineering, not UI; a demo page would be surface area with no evidentiary value for that |
| A tag-driven public deployment | FLY-IO-DEPLOYMENT.md | No public demo exists for this POC; `docker-compose.yml` covers local development only |

## Superseded

None yet.
