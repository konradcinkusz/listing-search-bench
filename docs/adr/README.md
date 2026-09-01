# Architecture decision records

One file per decision, numbered sequentially, never renumbered. A superseded ADR is
never deleted or rewritten — the record of what was believed at the time is the
point. New ADRs are copied from [`0000-template.md`](0000-template.md).

`0001` is the meta-decision to keep ADRs at all; `0002`–`0008` are the seven named
decisions this repository's design note calls out specifically.

| # | Title | Status | Date |
|---|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted | 2026-08-17 |
| [0002](0002-mock-first-zero-credential-default.md) | Mock index first, zero credentials by default | Accepted | 2026-08-17 |
| [0003](0003-assertions-are-structural-never-response-text.md) | Assertions read the structural trace, never response text | Accepted | 2026-08-17 |
| [0004](0004-pin-the-embedding-and-the-judge-model-separately.md) | Pin the embedding and the judge model separately, never fall back silently | Accepted | 2026-08-17 |
| [0005](0005-elasticsearch-and-vector-store-sdk-behind-one-method-seam.md) | The Elasticsearch and vector-store SDK lives behind a five-method seam | Accepted | 2026-08-17 |
| [0006](0006-event-idempotency-at-the-consumer-boundary.md) | Event idempotency at the consumer boundary | Accepted | 2026-08-17 |
| [0007](0007-render-the-overview-to-pdf-on-demand.md) | Render an overview document to PDF on demand, never committed | Superseded by 0008 | 2026-08-17 |
| [0008](0008-hand-authored-latex-editions-in-english-and-polish.md) | Hand-author the overview as LaTeX, in English and Polish | Accepted | 2026-09-01 |

## What belongs here versus `DEVIATIONS.md` versus nowhere

- **An ADR** records a decision this repository made and stands behind — including
  one it later reverses, which becomes a new ADR marked "Supersedes ADR-NNNN" rather
  than an edit to the old one.
- **A `DEVIATIONS.md` row** records a place this repository departs from
  `architecture-standards` or from the worked example it mirrors — dated, reasoned,
  with a closing condition.
- **Nothing** is what an unrecorded convention gets. If a reader would have to ask
  "why is it like this" in review, it belongs in one of the two files above.
