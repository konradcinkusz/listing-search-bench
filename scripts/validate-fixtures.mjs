#!/usr/bin/env node
// Validates every evals/fixtures/*.yaml against evals/schema/fixture.schema.json.

import { readFileSync, readdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import { parse } from "yaml";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const fixturesRoot = join(root, "evals", "fixtures");
const schemaPath = join(root, "evals", "schema", "fixture.schema.json");

function main() {
  const schema = JSON.parse(readFileSync(schemaPath, "utf8"));
  const ajv = new Ajv2020({ allErrors: true, strict: false });
  addFormats(ajv);
  const validate = ajv.compile(schema);

  const files = readdirSync(fixturesRoot).filter((name) => name.endsWith(".yaml"));

  if (files.length === 0) {
    console.error(`No fixture files found under ${fixturesRoot}.`);
    process.exit(1);
  }

  const errors = [];

  for (const name of files) {
    const path = join(fixturesRoot, name);
    const doc = parse(readFileSync(path, "utf8"));

    if (!validate(doc)) {
      for (const issue of validate.errors) {
        errors.push(`${name}: ${issue.instancePath || "(root)"} ${issue.message}`);
      }
      continue;
    }

    const ownerIds = new Set((doc.owners ?? []).map((o) => o.id));
    for (const listing of doc.listings) {
      if (!ownerIds.has(listing.owner_id)) {
        errors.push(`${name}: listing '${listing.id}' references unknown owner_id '${listing.owner_id}'`);
      }
    }

    const listingIds = new Set();
    for (const listing of doc.listings) {
      if (listingIds.has(listing.id)) {
        errors.push(`${name}: duplicate listing id '${listing.id}'`);
      }
      listingIds.add(listing.id);
    }
  }

  if (errors.length > 0) {
    console.error(`${errors.length} fixture validation error(s):\n`);
    for (const error of errors) console.error(`  - ${error}`);
    process.exit(1);
  }

  console.log(`${files.length} fixture file(s) validated.`);
}

main();
