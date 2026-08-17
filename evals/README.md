# evals/ — the eval corpus, as data

Nothing under this directory is .NET-specific. `schema/`, `fixtures/`, `scenarios/`,
`rubrics/`, `baselines/` and `calibration/` are YAML and JSON; the harness that reads
them (`Homefinder.Evals/`) is one project, and mostly two files in it —
[`Execution/ScenarioRunner.cs`](Homefinder.Evals/Execution/ScenarioRunner.cs) and
[`Assertions/AssertionEvaluator.cs`](Homefinder.Evals/Assertions/AssertionEvaluator.cs).

## The tour

| Directory | Contents |
|---|---|
| [`schema/`](schema) | `scenario.schema.json` and `fixture.schema.json` — strict JSON Schema, `additionalProperties: false` throughout |
| [`fixtures/`](fixtures) | `zurich-catalogue.yaml` — the shared base catalogue every scenario starts from unless it names another. Includes active, delisted, expired and draft listings, in English, French and German, plus one deliberately manipulative listing |
| [`scenarios/`](scenarios) | 26 scenarios in five directories — `happy/`, `ambiguity/`, `exclusion/`, `adversarial/`, `degradation/` — one file per scenario, filename matching the scenario's `id` |
| [`rubrics/`](rubrics) | `judge.yaml` (four rubrics, calibration gate) and `judge-prompt.md` (the static template) |
| [`calibration/`](calibration) | `labels.jsonl`, append-only human labels — currently empty, see [D-1](../docs/DEVIATIONS.md) |
| [`baselines/`](baselines) | `layer1.json` — recorded pass state, spec version and embedding function a regression is measured against |

## Reading a scenario

Every scenario has the same shape: an `id`, a `class`, a `gate` (`constraint` hard-
blocks at 100%, `behaviour` is measured against the baseline), a `title` and a `why`
stating what breaking it would catch, a `fixture` (base catalogue plus an optional
delta and fault-injection config), a list of `steps` (`search` or `ingest`), and a
list of `expect` assertions read against that run — see
[`docs/SPEC.md` §2](../docs/SPEC.md#2-vocabulary) for the full assertion vocabulary
and [`docs/SPEC.md` §4](../docs/SPEC.md#4-hard-constraints) for what a `constraint`
gate is actually checking.

## Running it

```bash
dotnet test --project Homefinder.Evals                                          # everything
dotnet test --project Homefinder.Evals --filter "FullyQualifiedName~Layer1Tests"    # scenarios only
dotnet test --project Homefinder.Evals --filter "FullyQualifiedName~MutationTests"  # the mutation pass only
```

Corpus mechanics — fixture composition, fault injection, the trace-capture seam —
are documented as code comments on `ScenarioRunner`, `FixtureComposer` and
`FaultInjectingSearchIndex`, on the theory that mechanics drift from prose faster
than they drift from the code sitting next to them.
