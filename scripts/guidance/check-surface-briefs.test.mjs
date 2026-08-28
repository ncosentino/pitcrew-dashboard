import assert from "node:assert/strict";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import { afterEach, test } from "node:test";

import {
  surfaceBriefPathForTarget,
  writeSurfaceBrief,
} from "../../.github/skills/impeccable/scripts/lib/surface-briefs.mjs";
import { checkSurfaceBriefs } from "./check-surface-briefs.mjs";

const roots = [];
const body = `## Scope and mode

Operate.

## Audience and job

Operator task.

## Hierarchy and interaction

- One task.

## Responsive behavior and states

All states.

## Direction and anti-goals

Clear and bounded.`;

function fixture() {
  const root = mkdtempSync(join(tmpdir(), "surface-briefs-"));
  roots.push(root);
  return root;
}

function writeSource(root, target) {
  const path = join(root, target);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, "export {};\n");
}

afterEach(() => {
  for (const root of roots.splice(0))
    rmSync(root, { recursive: true, force: true });
});

test("accepts shipped targets and explicitly planned future targets", () => {
  const root = fixture();
  const shipped = "src/Shipped.tsx";
  const planned = "src/Planned.tsx";
  writeSource(root, shipped);
  writeSurfaceBrief({ projectRoot: root, primaryTarget: shipped, body });
  const plannedPath = writeSurfaceBrief({
    projectRoot: root,
    primaryTarget: planned,
    body,
  });
  const plannedText = readFileSync(plannedPath, "utf8").replace(
    "version: 1\n",
    'version: 1\nstatus: "planned"\n',
  );
  writeFileSync(plannedPath, plannedText);

  assert.deepEqual(
    checkSurfaceBriefs(root, { expectedPrimaryTargets: [shipped, planned] }),
    { scanned: 2, violations: [] },
  );
});

test("rejects multiline related_targets that the production parser cannot load", () => {
  const root = fixture();
  const primary = "src/Primary.tsx";
  const related = "src/Related.tsx";
  writeSource(root, primary);
  writeSource(root, related);
  const briefPath = surfaceBriefPathForTarget(primary, { projectRoot: root });
  mkdirSync(dirname(briefPath), { recursive: true });
  const slug = basename(briefPath, ".md");
  writeFileSync(
    briefPath,
    `---
version: 1
slug: "${slug}"
primary_target: "${primary}"
related_targets:
  ["${related}"]
---

${body}
`,
  );

  const result = checkSurfaceBriefs(root, {
    expectedPrimaryTargets: [primary],
  });

  assert.deepEqual(
    result.violations.map(({ kind }) => kind),
    ["invalid-related-targets"],
  );
});

test("rejects one source target mapped by multiple surface briefs", () => {
  const root = fixture();
  const first = "src/First.tsx";
  const second = "src/Second.tsx";
  const shared = "src/Shared.tsx";
  for (const target of [first, second, shared]) writeSource(root, target);
  writeSurfaceBrief({
    projectRoot: root,
    primaryTarget: first,
    relatedTargets: [shared],
    body,
  });
  writeSurfaceBrief({
    projectRoot: root,
    primaryTarget: second,
    relatedTargets: [shared],
    body,
  });

  const result = checkSurfaceBriefs(root, {
    expectedPrimaryTargets: [first, second],
  });

  assert.equal(result.violations.length, 1);
  assert.equal(result.violations[0]?.kind, "ambiguous-target");
});

test("rejects missing shipped targets", () => {
  const root = fixture();
  const missing = "src/Missing.tsx";
  writeSurfaceBrief({ projectRoot: root, primaryTarget: missing, body });

  const result = checkSurfaceBriefs(root, {
    expectedPrimaryTargets: [missing],
  });

  assert.equal(result.violations.length, 1);
  assert.equal(result.violations[0]?.kind, "missing-shipped-target");
});
