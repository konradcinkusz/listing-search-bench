#!/usr/bin/env node
// Validates every evals/scenarios/**/*.yaml against evals/schema/scenario.schema.json
// (Ajv), then applies corpus invariants the schema alone cannot express — the same
// split agent-eval-bench's scripts/validate-scenarios.mjs makes: shape by schema,
// cross-file consistency by this script. The C# side (ScenarioLoader) also
// re-parses these files, loosely, and fails loudly only on what it cannot
// interpret — it does not re-implement schema validation, so this script is not
// redundant with `dotnet test`.
//
// Ajv's strict mode is off: the schema's `allOf`/`if`/`then` conditional-required
// blocks (action-dependent fields on a scenario step) trip its "required property
// not in properties at this level" heuristic even though the schema is correct —
// a known sharp edge of validating conditional requirements, not a relaxation of
// what gets checked.

import { readFileSync, readdirSync, existsSync } from "node:fs";
import { join, basename, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { parse } from "yaml";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const scenariosRoot = join(root, "evals", "scenarios");
const fixturesRoot = join(root, "evals", "fixtures");
const schemaPath = join(root, "evals", "schema", "scenario.schema.json");

const KNOWN_RUBRICS = new Set(["relevance", "attribution-clarity", "exclusion-honesty", "degradation-honesty"]);
const CLASS_PREFIX = { happy: "hap", ambiguity: "amb", exclusion: "exc", adversarial: "adv", degradation: "deg" };
const REQUIRED_CLASSES = Object.keys(CLASS_PREFIX);

function walk(dir) {
  const entries = readdirSync(dir, { withFileTypes: true });
  return entries.flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return walk(path);
    return entry.name.endsWith(".yaml") ? [path] : [];
  });
}

function main() {
  const errors = [];
  const schema = JSON.parse(readFileSync(schemaPath, "utf8"));
  const ajv = new Ajv2020({ allErrors: true, strict: false });
  addFormats(ajv);
  const validate = ajv.compile(schema);

  const files = walk(scenariosRoot);

  if (files.length === 0) {
    console.error(`No scenario files found under ${scenariosRoot} — an empty corpus passes every gate vacuously.`);
    process.exit(1);
  }

  const seenIds = new Map();
  const seenByClass = Object.fromEntries(REQUIRED_CLASSES.map((c) => [c, 0]));

  for (const file of files) {
    const relative = file.slice(root.length + 1);
    const yaml = readFileSync(file, "utf8");
    let doc;

    try {
      doc = parse(yaml);
    } catch (error) {
      errors.push(`${relative}: not valid YAML — ${error.message}`);
      continue;
    }

    if (!validate(doc)) {
      for (const issue of validate.errors) {
        errors.push(`${relative}: ${issue.instancePath || "(root)"} ${issue.message}`);
      }
      continue; // further checks assume schema-valid shape
    }

    const expectedFileName = `${doc.id}.yaml`;
    if (basename(file) !== expectedFileName) {
      errors.push(`${relative}: filename does not match id '${doc.id}' (expected '${expectedFileName}')`);
    }

    const dirName = basename(dirname(file));
    if (dirName !== doc.class) {
      errors.push(`${relative}: lives under '${dirName}/' but declares class '${doc.class}'`);
    }

    const expectedPrefix = CLASS_PREFIX[doc.class];
    if (expectedPrefix && !doc.id.startsWith(`${expectedPrefix}-`)) {
      errors.push(`${relative}: id '${doc.id}' does not start with '${expectedPrefix}-' for class '${doc.class}'`);
    }

    if (seenIds.has(doc.id)) {
      errors.push(`${relative}: duplicate id '${doc.id}', also used by ${seenIds.get(doc.id)}`);
    } else {
      seenIds.set(doc.id, relative);
    }

    if (REQUIRED_CLASSES.includes(doc.class)) {
      seenByClass[doc.class] += 1;
    }

    const fixturePath = join(fixturesRoot, `${doc.fixture.base}.yaml`);
    if (!existsSync(fixturePath)) {
      errors.push(`${relative}: fixture.base '${doc.fixture.base}' does not exist at ${fixturePath}`);
    }

    for (const rubric of doc.rubrics ?? []) {
      if (!KNOWN_RUBRICS.has(rubric)) {
        errors.push(`${relative}: unknown rubric '${rubric}'`);
      }
    }

    // A denial/exclusion-shaped claim needs an absence assertion, or it is a claim
    // about nothing — SPEC §4's two-assertion rule. Scoped to the exclusion class
    // specifically: not every adversarial scenario is exclusion-shaped (C-7's
    // ranking-manipulation defence is a rank-integrity property, proven by
    // result_ranked_below and candidate_set_includes together, not by an absence).
    if (doc.class === "exclusion" && doc.gate === "constraint") {
      const hasAbsence = doc.expect.some((a) =>
        ["result_excludes", "candidate_set_excludes", "event_not_emitted"].includes(a.assert));
      if (!hasAbsence) {
        errors.push(`${relative}: gate=constraint in class '${doc.class}' but no absence assertion (result_excludes / candidate_set_excludes / event_not_emitted)`);
      }
    }
  }

  for (const cls of REQUIRED_CLASSES) {
    if (seenByClass[cls] === 0) {
      errors.push(`No scenarios at all in class '${cls}' — every class in docs/SPEC.md §3.2 must be represented.`);
    }
  }

  if (errors.length > 0) {
    console.error(`${errors.length} scenario validation error(s):\n`);
    for (const error of errors) console.error(`  - ${error}`);
    process.exit(1);
  }

  console.log(`${files.length} scenarios validated across ${REQUIRED_CLASSES.length} classes.`);
}

main();
