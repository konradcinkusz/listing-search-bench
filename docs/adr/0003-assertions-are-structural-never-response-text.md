# ADR-0003: Assertions read the structural trace, never response text

- **Status**: Accepted
- **Date**: 2026-08-17
- **Phase**: 4
- **Relates to**: AI-EVALS.md §4, ADR-0003 of `agent-eval-bench`

## Context

`AssertionEvaluator` needs to decide, for every scenario, whether a listing was
included, excluded, correctly attributed, or correctly ranked. That information is
available two ways: parsing the JSON response body, or reading the trace
(`SearchResponse.Results`, `IndexCallRecord.CandidateListingIds`, span attributes,
events) that produced it.

## Decision

Every Layer 1 assertion reads a structured field on a trace record or a returned
DTO — a listing id, a rank, an event name, a typed span tag — and never a
description, a title, or any other free-text field. `response_excludes_internal_fields`
is the one apparent exception, and it proves the rule rather than breaking it: it
checks a `ListingId` against a fixed regex, never scans rendered JSON for a
number that looks like a score.

## Alternatives considered

### Snapshot the JSON response and diff it

**Why it is attractive:** Simple to write, catches "the response changed" in one
assertion instead of several targeted ones, and needs no trace instrumentation at
all.

**Why it lost:** It fails in both directions at once. It breaks on a harmless
change — reordering JSON keys, rounding a score to one more decimal place, adding a
field nobody asked to be notified about — and it passes a genuinely broken ranking
if the fields it happens to compare do not include rank. A snapshot test is only as
strict as what a human remembered to put in the golden file, and nobody re-derives
that file's completeness on every review.

### Regex over the rendered response for internal-looking identifiers

**Why it is attractive:** One assertion type instead of a typed-DTO guarantee;
works even if `SearchResultItem` grows a field nobody thought to check.

**Why it lost:** SPEC §2.6 names exactly this failure mode — a regex over prose is
the `HasText`-shaped defect `E2E-ACCEPTANCE-TESTING.md` §4 already cost the estate
once. A raw score is a `double` field that either exists on the DTO or does not;
that is checkable by the type system, and pattern-matching rendered text for
"something that looks like a score" is checkable by nothing.

## Consequences

**What this makes easy:** A ranking-weight tune, a rounding change, or a new field
on `SearchResultItem` never breaks an assertion that was not about any of those
things.

**What this makes hard:** Testing something the trace does not carry — every new
kind of claim a scenario needs to make is a new `SearchDiagnostics` attribute or
event first, and a new assertion type second.

**What we accept:** `AssertionEvaluator` is 15 assertion types and will grow before
it shrinks; a scenario schema and an evaluator that must agree exactly (SPEC's own
"unrecognised assertion is an error, not a pass" rule) is more code than one
JSON-diff function, in exchange for meaning something specific.

## Revisit when

A quality this document cannot state as a structural trace property needs testing —
at which point it belongs to Layer 2, not to a new exception in Layer 1.
