import { existsSync } from "node:fs";
import { basename, relative, resolve, sep } from "node:path";
import { pathToFileURL } from "node:url";

import {
  listSurfaceBriefs,
  SURFACE_BRIEF_VERSION,
  surfaceBriefPathForTarget,
} from "../../.github/skills/impeccable/scripts/lib/surface-briefs.mjs";

export const expectedSurfacePrimaryTargets = [
  "src/PitCrew.Dashboard.WebApi/ClientApp/src/core/routing/AuthenticatedShell.tsx",
  "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/FleetOverviewPage.tsx",
  "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/components/ProfileImageRollout.tsx",
  "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/fleet/pages/NodePages.tsx",
  "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/images/ImageWorkspace.tsx",
  "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/runners/RunnersPage.tsx",
  "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/settings/SettingsPages.tsx",
  "src/PitCrew.Dashboard.WebApi/ClientApp/src/features/support/SupportPage.tsx",
];

const requiredBodySections = [
  /^## Scope and mode$/m,
  /^## Audience and job$/m,
  /^## Hierarchy and interaction$/m,
  /^## (?:Responsive behavior and states|States and constraints)$/m,
  /^## Direction and anti-goals$/m,
];

function toPosix(path) {
  return path.split(sep).join("/");
}

function lineOf(text, pattern) {
  const lines = String(text).split(/\r?\n/);
  const index = lines.findIndex((line) => pattern.test(line));
  return index < 0 ? 1 : index + 1;
}

function violation(file, line, kind, message) {
  return { file, line, kind, message };
}

/** Validates exact Impeccable brief inventory, parser shape, target mappings, and shipped files. */
export function checkSurfaceBriefs(
  projectRoot = process.cwd(),
  { expectedPrimaryTargets = expectedSurfacePrimaryTargets } = {},
) {
  const root = resolve(projectRoot);
  const briefs = listSurfaceBriefs(root);
  const violations = [];
  const mappedTargets = new Map();
  const actualPrimaryTargets = new Set();

  for (const brief of briefs) {
    const file = toPosix(relative(root, brief.path));
    const status = brief.meta.status ?? "shipped";
    const expectedSlug = basename(brief.path, ".md");

    if (brief.meta.version !== SURFACE_BRIEF_VERSION) {
      violations.push(
        violation(
          file,
          lineOf(brief.text, /^version:/),
          "invalid-version",
          `version must be ${SURFACE_BRIEF_VERSION}.`,
        ),
      );
    }
    if (brief.slug !== expectedSlug) {
      violations.push(
        violation(
          file,
          lineOf(brief.text, /^slug:/),
          "invalid-slug",
          `slug must match filename "${expectedSlug}".`,
        ),
      );
    }
    if (!brief.primaryTarget) {
      violations.push(
        violation(
          file,
          lineOf(brief.text, /^primary_target:/),
          "missing-primary-target",
          "primary_target must be one project-relative source target.",
        ),
      );
      continue;
    }

    actualPrimaryTargets.add(brief.primaryTarget);
    const expectedPath = surfaceBriefPathForTarget(brief.primaryTarget, {
      projectRoot: root,
    });
    if (!expectedPath || resolve(expectedPath) !== resolve(brief.path)) {
      violations.push(
        violation(
          file,
          lineOf(brief.text, /^primary_target:/),
          "filename-target-mismatch",
          "filename must be derived from primary_target by the Impeccable surface parser.",
        ),
      );
    }
    if (!Array.isArray(brief.meta.related_targets)) {
      violations.push(
        violation(
          file,
          lineOf(brief.text, /^related_targets:/),
          "invalid-related-targets",
          "related_targets must be an inline JSON array so the Impeccable parser can load it.",
        ),
      );
    }
    if (status !== "shipped" && status !== "planned") {
      violations.push(
        violation(
          file,
          lineOf(brief.text, /^status:/),
          "invalid-status",
          'status must be "shipped" or "planned" when declared.',
        ),
      );
    }

    for (const target of brief.targets) {
      const previous = mappedTargets.get(target);
      if (previous) {
        violations.push(
          violation(
            file,
            lineOf(brief.text, /^(?:primary_target|related_targets):/),
            "ambiguous-target",
            `target "${target}" is already mapped by ${previous}.`,
          ),
        );
      } else {
        mappedTargets.set(target, file);
      }

      if (
        status === "shipped" &&
        !target.startsWith("route:") &&
        !/^https?:\/\//i.test(target) &&
        !existsSync(resolve(root, target))
      ) {
        violations.push(
          violation(
            file,
            lineOf(brief.text, /^(?:primary_target|related_targets):/),
            "missing-shipped-target",
            `shipped target "${target}" does not exist.`,
          ),
        );
      }
    }

    for (const section of requiredBodySections) {
      if (!section.test(brief.body)) {
        violations.push(
          violation(
            file,
            1,
            "missing-section",
            `brief body is missing required section ${section}.`,
          ),
        );
      }
    }
  }

  const expected = new Set(expectedPrimaryTargets);
  for (const target of expected) {
    if (!actualPrimaryTargets.has(target)) {
      violations.push(
        violation(
          ".impeccable/surfaces",
          1,
          "missing-brief",
          `expected primary target "${target}" has no surface brief.`,
        ),
      );
    }
  }
  for (const target of actualPrimaryTargets) {
    if (!expected.has(target)) {
      violations.push(
        violation(
          mappedTargets.get(target) ?? ".impeccable/surfaces",
          1,
          "unexpected-brief",
          `primary target "${target}" is not in the maintained surface inventory.`,
        ),
      );
    }
  }

  return { scanned: briefs.length, violations };
}

function formatViolation(item) {
  return [
    `  ${item.file}:${item.line}`,
    `    ${item.kind}`,
    `    ${item.message}`,
  ].join("\n");
}

const isCli =
  process.argv[1] !== undefined &&
  pathToFileURL(resolve(process.argv[1])).href === import.meta.url;

if (isCli) {
  const rootIndex = process.argv.indexOf("--root");
  const root = rootIndex >= 0 ? process.argv[rootIndex + 1] : process.cwd();
  const { scanned, violations } = checkSurfaceBriefs(root);
  if (violations.length > 0) {
    console.error(
      `check-surface-briefs: ${violations.length} violation(s) in ${scanned} brief(s):`,
    );
    for (const item of violations) console.error(formatViolation(item));
    process.exitCode = 1;
  } else {
    console.log(
      `check-surface-briefs: scanned ${scanned} brief(s); 0 violations.`,
    );
  }
}
