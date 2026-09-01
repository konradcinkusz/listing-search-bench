# ADR-0008: Hand-author the overview as LaTeX, in English and Polish

- **Status**: Accepted
- **Date**: 2026-09-01
- **Phase**: 5
- **Supersedes**: [ADR-0007](0007-render-the-overview-to-pdf-on-demand.md)
- **Relates to**: REPO-BASELINE.md, ADR-0006 of `agent-eval-bench`

## Context

[ADR-0007](0007-render-the-overview-to-pdf-on-demand.md) chose pandoc rendering
`docs/OVERVIEW.md` directly, and explicitly weighed and rejected the
hand-authored `.tex` presentation `agent-eval-bench` carries. It rejected it on
one argument — that reproducing that presentation would be "a disproportionate
investment in a rendering pipeline for a POC whose actual subject is a
search-ranking pipeline" — and named its own revisit condition: *"Pandoc's
default rendering becomes a real limitation — a diagram, a layout need, or a
length that the default LaTeX template cannot serve well."*

Two things changed.

**The diagram limitation arrived.** `README.md` carries two Mermaid diagrams and
the architecture warranted two more. Pandoc's GFM reader does not render Mermaid;
a fenced ` ```mermaid ` block reaches the PDF as a verbatim listing of its own
source. The overview's PDF was therefore the one presentation of this repository
with no pictures in it at all, which is the wrong way round: the PDF is the
artifact someone reads linearly, away from GitHub, and it is where a diagram is
worth the most.

**The audience is not monolingual.** The overview is the document handed to
someone who wants the whole shape of the repository in one sitting, and the
people who read it do not all read the same language. A single English PDF served
half of them.

ADR-0007 also anticipated exactly this file: *"If a future revision wants the
fuller presentation, that is a new decision — logged as a new ADR superseding
this one."*

## Decision

`docs/OVERVIEW.md` remains the source of truth; its PDF presentation is now two
hand-authored LaTeX documents — `docs/papers/listing-search-bench-overview.tex`
and `listing-search-bench-overview.pl.tex` — built on demand into one artifact
carrying both language editions, and the figures in them are this repository's
own `docs/diagrams/*.mmd` rendered to vector PDF rather than redrawn.

## Alternatives considered

### Keep pandoc and add a Polish Markdown twin

**Why it is attractive:** No LaTeX toolchain, no new build step; a second
`OVERVIEW.pl.md` would be the smallest possible change, and the parity between
the two halves could be guarded mechanically the way `agent-eval-bench` guards
its bilingual documents.

**Why it lost:** It solves the language half and leaves the diagram half exactly
where it was — the PDFs would still have no pictures, and the revisit condition
ADR-0007 named would still be open. It also doubles the number of Markdown
documents a reader might land on from GitHub, where one canonical
`docs/OVERVIEW.md` is currently the front door.

### Hand-author the `.tex`, but draw the diagrams in TikZ

**Why it is attractive:** Full control of every line on the page, and no Node
dependency in the PDF build.

**Why it lost:** Two sources of truth for one picture. README's Mermaid and a
TikZ recreation would drift the first time either changed, and nothing would go
red. Rendering the same `.mmd` files both places keeps one source and costs a
`npm ci && npm run render:diagrams` step, which the workflow already had a
reason to run.

## Consequences

**What this makes easy:** A PDF with the repository's actual architecture
diagrams in it, in the house style shared with the author's other formal
documents, in either language. The figures cannot drift from README's, because
`scripts/check-diagrams.mjs` fails the build if an inline block and its `.mmd`
disagree, and the papers include the rendered form of that same file.

**What this makes hard:** Exactly what ADR-0007 predicted. There are now two
hand-maintained presentations of `docs/OVERVIEW.md`, and neither is generated
from it, so a change to the Markdown does not reach either automatically. That
cost is real and is not mechanically guarded today.

**What we accept:** That drift risk, deliberately, in exchange for a
presentation the overview's actual audience can read. It is narrowed rather than
removed: each paper's header comment names `docs/OVERVIEW.md` as its source of
truth and states that it introduces no fact the Markdown does not already
contain, so the papers are a presentation of a document rather than a second
document with its own claims.

## Revisit when

The two editions drift from `docs/OVERVIEW.md` in a way a reader notices — at
which point the answer is a parity check in `npm run lint` covering the papers,
in the shape `check-diagrams.mjs` already uses for diagrams and
`agent-eval-bench`'s `check-doc-parity.mjs` uses for its bilingual Markdown, not
a return to a rendering pipeline that cannot draw the diagrams.
