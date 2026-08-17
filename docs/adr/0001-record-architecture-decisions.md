# ADR-0001: Record architecture decisions

- **Status**: Accepted
- **Date**: 2026-08-17
- **Phase**: 0
- **Relates to**: P14 (documentation records reasoning, not just steps)

## Context

A decision made in a pull-request description is a decision that is one force-push
away from vanishing. A decision made in a comment is a decision nobody reviews
without first finding the code it explains. This repository makes architectural
decisions the way `agent-eval-bench` does, and inherits the reason rather than
re-deriving it.

## Decision

Significant architectural decisions are recorded as numbered, immutable markdown
files under `docs/adr/`, one per decision, following
[`0000-template.md`](0000-template.md).

## Alternatives considered

### Wiki pages

**Why it is attractive:** Easier to edit, searchable, no pull request required.

**Why it lost:** Editable is the problem, not the feature — a wiki page silently
edited after the fact is a decision rewritten with hindsight, which is a different
document wearing the old one's name. A wiki also lives outside the commit that
implements the decision, so the two drift out of review together.

## Consequences

**What this makes easy:** Answering "why is it like this" without asking the person
who wrote it, months later, from memory.

**What this makes hard:** Nothing — an ADR is cheap to write and this repository is
small enough that the discipline never competes with shipping.

**What we accept:** A small amount of ceremony for decisions that turn out, in
hindsight, not to have needed recording. That is cheaper than the alternative.

## Revisit when

Never, structurally — this is the ADR about having ADRs.
