<!--
  Reviews without repro and impact are the reason this template exists
  (REPO-BASELINE.md §1). Delete the sections that genuinely do not apply — but
  delete them, do not leave them blank: a blank section reads as "not checked".
-->

## What changed

<!-- One paragraph. What behaviour is different after this PR than before it? -->

## Why

<!--
  The reasoning, not the steps (P14). If this PR argues *against* something —
  a rejected alternative, a deviation from the standards — say so here and link
  the ADR that records it.
-->

## Spec impact

<!--
  Spec before code, always. AI-EVALS.md §2 makes the spec the thing an edit is
  reviewed against; if implementation revealed the spec was wrong, the spec is
  amended in THIS PR and the amendment is described here, with the version at the
  top of docs/SPEC.md bumped.
-->

- [ ] No behaviour change — `docs/SPEC.md` untouched
- [ ] Behaviour change, and `docs/SPEC.md` is amended in this PR
- [ ] New hard constraint added (and it has a scenario that fails without it)

## Eval impact

<!--
  The eval-triggering paths in this repository are `evals/`, `docs/SPEC.md`, and
  the pipeline itself — `src/ListingSearch.Service/{Pipeline,Search,Ingestion}/`.
  A change to any of them changes what the suite measures. State what the run showed.
-->

- [ ] Constraint scenarios: 100% pass (hard gate — no exceptions, no "flaky")
- [ ] Behaviour scenarios: pass rate at or above `evals/baselines/layer1.json`
- [ ] Mutation pass: all four variants still caught
- [ ] Baseline re-recorded, and the diff is in this PR for review

**Scenario diff vs baseline:**

<!-- Paste the eval job's diff, or write "no eval-triggering paths touched". -->

## Verification

<!--
  What did you actually run, and what did it print? "Tests pass" is not evidence;
  the output is. A scenario that executed nothing FAILS — it does not skip
  (E2E-ACCEPTANCE-TESTING.md §2).
-->

- [ ] `dotnet build ListingSearch.slnx` clean (warnings are errors in this repo)
- [ ] `dotnet test` green — unit, Layer 1, mutation pass, judge machinery
- [ ] `npm run lint` green — markdown, diagram parity, scenario and fixture schemas
- [ ] Fresh-clone check still true: `git clone && dotnet test` works with **zero** credentials

## Documentation impact

<!--
  ADR-0008 accepted a known drift risk: docs/OVERVIEW.md has two hand-authored
  LaTeX presentations (docs/papers/*.tex) that do NOT regenerate from it. Nothing
  guards that pair mechanically yet, which is exactly why it is a checklist line.
-->

- [ ] `docs/OVERVIEW.md` untouched, or both papers updated to match it
- [ ] Numbers still trace to `docs/FINDINGS.md` §1 rather than being copied forward
- [ ] A changed diagram was changed in `docs/diagrams/*.mmd` (README's inline copy
      follows; `npm run lint` fails if the two disagree)

## Standards conformance

<!--
  Where this repository must deviate from the standards it follows, the deviation is
  recorded — dated and reasoned — in `docs/DEVIATIONS.md`, and where it is worth
  generalising, an amendment is proposed back to `architecture-standards`.
-->

- [ ] No new deviation from `00-REFERENCE-ARCHITECTURE.md`
- [ ] New deviation, recorded in `docs/DEVIATIONS.md` with a date and a reason
- [ ] Amendment proposed back to `architecture-standards`

## Public-repo checks

<!-- This repository is public. Every commit is disclosed the moment it is pushed. -->

- [ ] No secrets, tokens, or credentials — including in comments and test fixtures
- [ ] No real-world listing data; every fixture is synthetic
- [ ] No client or customer names
- [ ] English only
