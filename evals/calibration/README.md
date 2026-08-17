# Calibration labels

`labels.jsonl` is append-only: one JSON object per line, each a human label of one
rubric score for one scenario, recorded **before** seeing the judge's score for that
same trace — anchoring bias runs upward otherwise, and a label written after seeing
the judge's answer measures agreement with nothing.

Shape:

```json
{"scenario_id": "hap-001-price-and-city-hard-filter", "rubric": "relevance", "score": 3, "labeller": "<handle>", "date": "2026-08-17"}
```

Current state: **empty.** No live judge run has ever produced a score to label
against (`docs/DEVIATIONS.md` D-4), so there is nothing yet to calibrate. The four
gating conditions in `evals/rubrics/judge.yaml`'s `calibration` block — at least 40
labels, across at least 8 distinct scenarios, an unweighted Cohen's kappa of at least
0.6, recorded under the repository owner's own handle rather than an AI rater's — are
therefore all unmet, and `Layer2Run` reports `skipped:no-credential` rather than a
score. `Judging/Calibration.cs` and `JudgeMachineryTests.cs` test the *arithmetic*
against hand-built label pairs, which needs no live judge and no labels file to be
non-empty; they are not a substitute for this file having entries in it.
