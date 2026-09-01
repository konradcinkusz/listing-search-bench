# ADR-0007: Render a project overview to PDF on demand, never committed

- **Status**: Superseded by [ADR-0008](0008-hand-authored-latex-editions-in-english-and-polish.md)
- **Date**: 2026-08-17
- **Phase**: 5
- **Relates to**: REPO-BASELINE.md, ADR-0006 of `agent-eval-bench`

## Context

A README is the right length for someone deciding whether to keep reading; it is
the wrong shape for someone who wants one linear document to read start to end, or
to attach to an application. `agent-eval-bench` solves this with `docs/OVERVIEW.md`
plus a LaTeX build rendered to PDF on a `workflow_dispatch`-only GitHub Action,
never committed to the repository. This repository has now written the overview
document (`docs/OVERVIEW.md`) and the render pipeline that decision describes
(`.github/workflows/build-overview-pdf.yml`).

## Decision

`docs/OVERVIEW.md` is the single source, rendered to PDF by a LaTeX build
triggered manually (not on every push), with the artifact never committed to
version control — a generated binary in git is a merge conflict waiting to
happen and a diff nobody can review.

**Where this repository's build differs from `agent-eval-bench`'s, stated rather
than left implicit:** `agent-eval-bench` hand-authors a `.tex` presentation (its
own house LaTeX template, custom TikZ diagrams) as a second document alongside
`docs/OVERVIEW.md`, built with `xu-cheng/latex-action`. This repository's build
instead renders `docs/OVERVIEW.md` itself directly to PDF via
[pandoc](https://pandoc.org/)'s LaTeX backend (`pdflatex`) — one source document,
no second hand-maintained presentation to keep in sync with it. This is a smaller
commitment for a smaller repository, not a claim that it produces the same visual
result; "a LaTeX build" is satisfied literally (pandoc's default PDF engine is
LaTeX), without asserting the two repositories' overview documents look alike.

## Alternatives considered

### Write the overview as a second README instead of a renderable document

**Why it is attractive:** No LaTeX toolchain, no build pipeline, one document
instead of two to keep in sync.

**Why it lost:** A README optimised for GitHub's rendering (badges, collapsible
sections, relative links) is a document that degrades badly as a PDF, and a
document written for linear reading degrades badly as a scannable landing page.
`agent-eval-bench`'s own resolution — one source document, two renderers, neither
compromised for the other — is the one this ADR adopts rather than re-deriving.

### Commit the built PDF to the repository so it never needs regenerating

**Why it is attractive:** Nothing to build, a reader gets the file immediately.

**Why it lost:** A committed binary artifact drifts from the source the moment the
source changes and nobody remembers to rebuild it, and git handles binary diffs
badly enough that reviewing a PDF's change is effectively impossible. Building on
demand means the PDF is never more than one manual trigger away from being current
LibreOffice.

### Hand-author a second `.tex` presentation, the way `agent-eval-bench` does

**Why it is attractive:** Full control over layout, custom diagrams (TikZ) drawn
for the page rather than reflowed from Markdown, and visual parity with the other
repository's own formal documents.

**Why it lost, for this repository specifically:** A second hand-maintained
document is a second thing that can drift from `docs/OVERVIEW.md`, and
`agent-eval-bench`'s presentation reached its current form over considerably more
effort than this repository has spent on documentation overall — reproducing it
here would be a disproportionate investment in a rendering pipeline for a POC
whose actual subject is a search-ranking pipeline, not a publishing pipeline.
Pandoc rendering `docs/OVERVIEW.md` directly keeps exactly one source document,
at the cost of less layout control — a trade this ADR accepts explicitly rather
than silently under-delivering on the original decision's spirit.

## Consequences

**What this makes easy:** One source document (`docs/OVERVIEW.md`) with no second
hand-maintained presentation that can drift from it; a reader can get either the
Markdown (GitHub-rendered) or the PDF from the exact same file.

**What this makes hard:** Layout control. Pandoc's default LaTeX template handles
headings, tables, and a table of contents well; it does not reproduce
`agent-eval-bench`'s custom diagrams or house colour scheme, and this repository's
overview has none of its own to lose.

**What we accept:** A plainer PDF than the worked example's, in exchange for one
source document instead of two. If a future revision wants the fuller
presentation, that is a new decision — logged as a new ADR superseding this one,
per this repository's own rule for changed decisions — not a silent upgrade to
this file.

## Revisit when

Pandoc's default rendering becomes a real limitation — a diagram, a layout need,
or a length that the default LaTeX template cannot serve well. At that point this
ADR is superseded by whichever presentation approach actually solves it, most
likely the hand-authored `.tex` alternative above.
