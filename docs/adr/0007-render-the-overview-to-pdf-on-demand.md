# ADR-0007: Render a project overview to PDF on demand, never committed

- **Status**: Proposed
- **Date**: 2026-08-17
- **Phase**: 5
- **Relates to**: REPO-BASELINE.md, ADR-0006 of `agent-eval-bench`

## Context

A README is the right length for someone deciding whether to keep reading; it is
the wrong shape for someone who wants one linear document to read start to end, or
to attach to an application. `agent-eval-bench` solves this with `docs/OVERVIEW.md`
plus a LaTeX build rendered to PDF on a `workflow_dispatch`-only GitHub Action,
never committed to the repository. This repository has not yet written the
overview document or the render pipeline that decision describes.

## Decision

When an overview document is written, it is rendered to PDF the same way:
`docs/OVERVIEW.md` as the single source, a LaTeX build triggered manually (not on
every push), and the artifact never committed to version control — a generated
binary in git is a merge conflict waiting to happen and a diff nobody can review.

This ADR is recorded as **Proposed** rather than **Accepted** deliberately: it
states the decision this repository would make if and when it builds the overview
document, without claiming the document or the pipeline already exist. Recording
the decision before the artifact avoids the alternative of building a rendering
pipeline first and only then deciding what it should do.

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

## Consequences

**What this makes easy:** Once written, the overview document has one source of
truth and no risk of a committed PDF silently going stale.

**What this makes hard:** Nothing yet — there is no pipeline to maintain.

**What we accept:** This decision currently governs a document that does not exist.
That is stated here rather than implied by silence, the same way this repository's
other gaps are `docs/DEVIATIONS.md` rows rather than absences nobody explains.

## Revisit when

`docs/OVERVIEW.md` is written. At that point this ADR moves to **Accepted** or is
superseded by whatever the actual build turns out to need.
