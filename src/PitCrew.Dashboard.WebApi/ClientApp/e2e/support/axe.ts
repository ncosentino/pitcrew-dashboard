/**
 * Runs axe-core against the current page and classifies findings.
 *
 * Per issue #84 / ADR-0007, Impeccable/axe findings stay advisory in this
 * harness: the suite must not be blocked on defects the ADR already
 * documents, but any *new* serious/critical violation must still fail so
 * regressions are caught. Classification happens per violation *node*, not
 * per rule ID: `KNOWN_BASELINE_COLOR_CONTRAST_NODE_HTML` allowlists the two
 * exact known markup variants of the brand "Dashboard" label, and
 * `KNOWN_BASELINE_COLOR_CONTRAST_COLOR_PAIRS` allowlists exact known
 * foreground/background color pairs for the remaining dark-theme
 * destructive token defect whose markup varies by instance. A
 * `color-contrast` violation on any other element, or any other color
 * pair, is still `unexpected`.
 */
import { AxeBuilder } from '@axe-core/playwright';
import type { Page, TestInfo } from '@playwright/test';
import type { CheckResult, NodeResult, Result } from 'axe-core';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const artifactRoot = path.join(process.cwd(), 'e2e', '.artifacts', 'axe');

/**
 * Exact outerHTML of the only two markup variants (compact sidebar lockup
 * and hero/login lockup — see `PitCrewBrand.tsx`) that previously rendered the
 * brand teal "Dashboard" label. Issue #86 repaired the contrast by switching
 * to `--brand-teal-accessible`. The baseline set is empty until the next
 * pre-existing defect is observed and classified.
 */
export const KNOWN_BASELINE_COLOR_CONTRAST_NODE_HTML: ReadonlySet<string> = new Set([]);

/**
 * Exact known `background-color`/`foreground-color` hex pairs (as reported
 * by axe's own contrast check `data.bgColor`/`data.fgColor`) for previously
 * failing dark-theme token pairs. Issue #86 corrected the dark-theme
 * `--destructive` value to pass AA with white text. The baseline set is
 * empty until a new pre-existing defect is observed.
 * Format: `${bgColor}|${fgColor}` (both lowercase hex, exactly as axe
 * reports them).
 */
export const KNOWN_BASELINE_COLOR_CONTRAST_COLOR_PAIRS: ReadonlySet<string> = new Set([]);

export interface AxeCheckResult {
  readonly all: Result[];
  readonly seriousOrCritical: Result[];
  readonly baseline: Result[];
  readonly unexpected: Result[];
}

interface ContrastCheckData {
  readonly bgColor?: string;
  readonly fgColor?: string;
}

function isContrastCheckData(data: unknown): data is ContrastCheckData {
  return typeof data === 'object' && data !== null;
}

/** Extracts the `bgColor`/`fgColor` axe reported for this node's contrast check, if any. */
function extractContrastColorPair(node: NodeResult): string | undefined {
  const checks: CheckResult[] = [...node.any, ...node.all, ...node.none];
  for (const check of checks) {
    if (check.id !== 'color-contrast' || !isContrastCheckData(check.data)) continue;
    const { bgColor, fgColor } = check.data;
    if (typeof bgColor === 'string' && typeof fgColor === 'string') {
      return `${bgColor}|${fgColor}`;
    }
  }
  return undefined;
}

function isKnownBaselineNode(violationId: string, node: NodeResult): boolean {
  if (violationId !== 'color-contrast') return false;
  if (KNOWN_BASELINE_COLOR_CONTRAST_NODE_HTML.has(node.html)) return true;
  const colorPair = extractContrastColorPair(node);
  return colorPair !== undefined && KNOWN_BASELINE_COLOR_CONTRAST_COLOR_PAIRS.has(colorPair);
}

/**
 * Pure classification: splits each serious/critical violation into a
 * baseline-only and/or unexpected-only reconstruction, based on which
 * individual `nodes` entries match a known baseline. A violation with a mix
 * of known and unknown nodes appears in both `baseline` and `unexpected`
 * rather than being tolerated wholesale. Exported (and browser-independent)
 * so it can be exercised directly in `e2e/axe-classification.spec.ts`
 * without a live page.
 */
export function classifyAxeResults(violations: Result[]): AxeCheckResult {
  const seriousOrCritical = violations.filter(
    (violation) => violation.impact === 'serious' || violation.impact === 'critical',
  );

  const baseline: Result[] = [];
  const unexpected: Result[] = [];
  for (const violation of seriousOrCritical) {
    const baselineNodes = violation.nodes.filter((node) => isKnownBaselineNode(violation.id, node));
    const unexpectedNodes = violation.nodes.filter(
      (node) => !isKnownBaselineNode(violation.id, node),
    );
    if (baselineNodes.length > 0) {
      baseline.push({ ...violation, nodes: baselineNodes });
    }
    if (unexpectedNodes.length > 0) {
      unexpected.push({ ...violation, nodes: unexpectedNodes });
    }
  }

  return { all: violations, seriousOrCritical, baseline, unexpected };
}

function sanitizeName(name: string): string {
  return name.replace(/[^a-z0-9-]+/gi, '-').toLowerCase();
}

/**
 * Runs the axe scan, writes a JSON artifact under `e2e/.artifacts/axe/`, and
 * attaches the same payload to the Playwright test report. Returns the
 * classified violation sets; callers assert `unexpected` is empty and may
 * separately record `baseline` as observed evidence.
 */
export async function runAxeCheck(
  page: Page,
  testInfo: TestInfo,
  artifactName: string,
): Promise<AxeCheckResult> {
  const results = await new AxeBuilder({ page }).analyze();
  const { seriousOrCritical, baseline, unexpected } = classifyAxeResults(results.violations);

  await mkdir(artifactRoot, { recursive: true });
  const fileName = `${sanitizeName(artifactName)}.json`;
  const payload = JSON.stringify(
    {
      url: results.url,
      testedAt: new Date().toISOString(),
      violations: results.violations,
      baselineNodeCounts: baseline.map((violation) => ({
        id: violation.id,
        nodeCount: violation.nodes.length,
      })),
    },
    null,
    2,
  );
  await writeFile(path.join(artifactRoot, fileName), payload, 'utf8');
  await testInfo.attach(`axe-${sanitizeName(artifactName)}`, {
    body: payload,
    contentType: 'application/json',
  });

  return { all: results.violations, seriousOrCritical, baseline, unexpected };
}
