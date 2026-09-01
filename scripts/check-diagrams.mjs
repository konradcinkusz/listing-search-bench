#!/usr/bin/env node
/**
 * check-diagrams.mjs — every Mermaid diagram exists in exactly the places it
 * should, and the copies agree.
 *
 * Why two places at all:
 *
 *   README.md must carry a diagram's source inline, because that is the only
 *   form GitHub renders. docs/diagrams/*.mmd must exist separately, because the
 *   LaTeX papers in docs/papers/ include these diagrams as rendered vector PDFs
 *   (scripts/render-diagrams.mjs), and a PDF cannot read a fenced code block
 *   out of a Markdown file.
 *
 *   Two copies of anything is a drift surface, and the failure mode is
 *   specific: somebody fixes the pipeline diagram in README.md, the paper keeps
 *   printing last month's pipeline, and nothing goes red. This repository's
 *   answer to a drift surface is never "remember to update both"; it is a check
 *   that fails.
 *
 * The two rules:
 *
 *   R1  Every ```mermaid block in README.md is byte-identical to some
 *       docs/diagrams/*.mmd. An inline diagram with no file behind it cannot be
 *       reused — not in a paper, not in a slide, not in an issue.
 *
 *   R2  Every docs/diagrams/*.mmd is actually used: inline in README.md, or
 *       included by a paper under docs/papers/ as
 *       ../diagrams/rendered/<slug>.pdf. A diagram nothing references is dead
 *       weight that still has to be kept correct.
 *
 * Not every .mmd appears in README: some exist only for the papers, which is why
 * R2 accepts either home rather than demanding both.
 */
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = dirname(dirname(fileURLToPath(import.meta.url)));
const DIAGRAMS = join(ROOT, 'docs', 'diagrams');
const PAPERS = join(ROOT, 'docs', 'papers');
const README = join(ROOT, 'README.md');

/** Trailing whitespace and a trailing newline are not a difference worth failing over. */
const normalise = (s) => s.replace(/\r\n/g, '\n').replace(/[ \t]+$/gm, '').trim();

const failures = [];

const files = existsSync(DIAGRAMS)
  ? readdirSync(DIAGRAMS).filter((f) => f.endsWith('.mmd')).sort()
  : [];

if (files.length === 0) {
  console.error('check-diagrams: no .mmd files found under docs/diagrams/');
  process.exit(1);
}

const byContent = new Map();
for (const file of files) {
  byContent.set(normalise(readFileSync(join(DIAGRAMS, file), 'utf8')), file);
}

// ---- R1: every inline block in README.md has a file behind it ---------------
const readme = readFileSync(README, 'utf8');
const inline = [...readme.matchAll(/```mermaid\n([\s\S]*?)```/g)].map((m) => m[1]);
const inlineFiles = new Set();

inline.forEach((block, i) => {
  const match = byContent.get(normalise(block));
  if (match) {
    inlineFiles.add(match);
  } else {
    failures.push(
      `R1  README.md mermaid block #${i + 1} matches no file in docs/diagrams/.\n` +
      '    Either it drifted from its .mmd, or it was added inline without one.\n' +
      `    First line: ${normalise(block).split('\n')[0]}`,
    );
  }
});

// ---- R2: every file is used somewhere --------------------------------------
const paperText = existsSync(PAPERS)
  ? readdirSync(PAPERS).filter((f) => f.endsWith('.tex'))
      .map((f) => readFileSync(join(PAPERS, f), 'utf8')).join('\n')
  : '';

for (const file of files) {
  const slug = file.replace(/\.mmd$/, '');
  if (inlineFiles.has(file)) continue;
  if (paperText.includes(`../diagrams/rendered/${slug}.pdf`)) continue;
  failures.push(
    `R2  docs/diagrams/${file} is referenced by nothing.\n` +
    '    Inline it in README.md, include it from a paper in docs/papers/, or delete it.',
  );
}

if (failures.length > 0) {
  console.error('check-diagrams: FAILED\n');
  for (const f of failures) console.error(`${f}\n`);
  process.exit(1);
}

console.log(
  `check-diagrams: ok — ${files.length} diagram(s), ` +
  `${inline.length} inline in README.md, all accounted for.`,
);
