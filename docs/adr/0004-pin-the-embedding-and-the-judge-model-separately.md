# ADR-0004: Pin the embedding and the judge model separately, never fall back silently

- **Status**: Accepted
- **Date**: 2026-08-17
- **Phase**: 1 (embedding), 5 (judge)
- **Relates to**: ADR-0004 of `agent-eval-bench`

## Context

Two independent numbers this repository could quietly let drift: which function
turns text into a vector for the retrieval path, and which model scores a Layer 2
rubric. Either changing without anyone deciding it changed would move every ranking
and every judge score for a reason nobody wrote down.

## Decision

The vector path uses exactly one embedding function,
`DeterministicTextEmbedding` — a fixed, deterministic feature hash, never a live
model call, and never silently swapped for one. `evals/baselines/layer1.json`
records which one (`"embedding": "deterministic-hash-v1"`) the same way it records
the spec version, and a baseline recorded against one embedding is never compared
against a run using another.

Separately, `JudgeConfiguration` pins the rubric and prompt **files** (hashed,
SPEC §2.3) but deliberately does **not** pin a judge model name in configuration.
`JudgeVerdict.Model` is read back from whatever `ILlmProvider.CompleteAsync`
reports the response actually came from — never assumed, never defaulted — so a
score is always attributable to the model that produced it, and a provider that
silently upgraded its model would be visible in every subsequent report rather than
hidden behind a config value nobody re-reads.

## Alternatives considered

### Automatic fallback: try the primary embedding model, fall back to a cheaper one on failure

**Why it is attractive:** Production-sensible — resilience against one model
provider's outage.

**Why it lost:** For an eval corpus, a fallback that fires silently mid-run means
the baseline this run is compared against describes a system nobody chose for this
run. `agent-eval-bench`'s ADR-0004 makes the same trade for its LLM provider for the
identical reason: a fallback is permitted only if the model that actually answered
is recorded on the result, never silently assumed to be the configured one.

### Pin the judge model in `judge.yaml` alongside the rubrics

**Why it is attractive:** One file, one hash, matches how the rubrics and prompt are
already pinned.

**Why it lost:** A pinned model name in a config file describes what was *requested*,
not what *answered*. The two can differ — a provider can route a request to a
different model version than the one named — and pinning only the request would let
that divergence go unrecorded. Reading it back from the response is the only source
that cannot lie about what actually ran.

## Consequences

**What this makes easy:** A Layer 1 baseline and a Layer 2 report are each
attributable to exactly the measuring instrument that produced them, always.

**What this makes hard:** Nothing today — no live judge run has ever happened
(D-1), so this ADR currently governs a fallback path that has never fired and a
`Model` field that has never been populated by anything but a unit test's fixture
value.

**What we accept:** The embedding function is a crude one (D-1). Pinning it
faithfully is a promise about reproducibility, not about quality.

## Revisit when

A real `ILlmProvider` implementation exists and a first keyed run populates
`JudgeVerdict.Model` for the first time.
