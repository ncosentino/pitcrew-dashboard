/**
 * Pure-logic proof that axe classification is scoped to exact known nodes,
 * not whole rule IDs. Uses a hand-built `Result[]` (no live page/browser
 * needed — `test` callbacks that don't destructure `{ page }` skip that
 * fixture) so this runs fast and deterministically.
 */
import { test, expect } from '@playwright/test';
import type { CheckResult, NodeResult, Result } from 'axe-core';

import {
  classifyAxeResults,
  KNOWN_BASELINE_COLOR_CONTRAST_COLOR_PAIRS,
  KNOWN_BASELINE_COLOR_CONTRAST_NODE_HTML,
} from './support/axe';

function makeNode(html: string, target: string[] = ['.some-selector']): NodeResult {
  return {
    html,
    target,
    failureSummary: 'synthetic node for classification testing',
    any: [],
    all: [],
    none: [],
  } as unknown as NodeResult;
}

/** Builds a node carrying axe's own reported contrast check `data.bgColor`/`data.fgColor`. */
function makeContrastNode(
  html: string,
  bgColor: string,
  fgColor: string,
  target: string[] = ['.some-selector'],
): NodeResult {
  const check: CheckResult = {
    id: 'color-contrast',
    data: { bgColor, fgColor },
  } as unknown as CheckResult;
  return {
    html,
    target,
    failureSummary: 'synthetic node for classification testing',
    any: [check],
    all: [],
    none: [],
  } as unknown as NodeResult;
}

function makeViolation(id: string, impact: Result['impact'], nodes: NodeResult[]): Result {
  return {
    id,
    impact,
    nodes,
    description: 'synthetic violation for classification testing',
    help: 'synthetic',
    helpUrl: 'https://example.com',
    tags: [],
  } as unknown as Result;
}

const [knownBaselineHtml] = KNOWN_BASELINE_COLOR_CONTRAST_NODE_HTML;

test('a violation containing only the known baseline node is classified as baseline', () => {
  const violations = [makeViolation('color-contrast', 'serious', [makeNode(knownBaselineHtml)])];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(1);
  expect(result.unexpected).toHaveLength(0);
  expect(result.baseline[0]?.nodes).toHaveLength(1);
});

test('a synthetic second color-contrast node that is not the known brand label is unexpected', () => {
  const syntheticUnknownNode = makeNode('<button class="bg-red-500 text-red-600">Danger</button>', [
    '.synthetic-unknown-node',
  ]);
  const violations = [
    makeViolation('color-contrast', 'serious', [makeNode(knownBaselineHtml), syntheticUnknownNode]),
  ];

  const result = classifyAxeResults(violations);

  // The known node is still recognized as baseline...
  expect(result.baseline).toHaveLength(1);
  expect(result.baseline[0]?.nodes).toHaveLength(1);
  expect(result.baseline[0]?.nodes[0]?.html).toBe(knownBaselineHtml);

  // ...but the synthetic, unrecognized node must NOT be silently tolerated
  // just because it shares a rule ID with a known baseline finding.
  expect(result.unexpected).toHaveLength(1);
  expect(result.unexpected[0]?.nodes).toHaveLength(1);
  expect(result.unexpected[0]?.nodes[0]?.html).toBe(syntheticUnknownNode.html);
});

test('a color-contrast violation with no known nodes at all is entirely unexpected', () => {
  const violations = [
    makeViolation('color-contrast', 'critical', [
      makeNode('<span class="text-gray-300 bg-gray-200">low contrast</span>'),
    ]),
  ];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(1);
});

test('a non-color-contrast serious violation is never baseline, even with matching HTML', () => {
  const violations = [makeViolation('some-other-rule', 'serious', [makeNode(knownBaselineHtml)])];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(1);
});

test('moderate/minor impact violations never enter serious/critical classification', () => {
  const violations = [makeViolation('color-contrast', 'moderate', [makeNode(knownBaselineHtml)])];

  const result = classifyAxeResults(violations);

  expect(result.seriousOrCritical).toHaveLength(0);
  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(0);
});

const [knownBaselineColorPair] = KNOWN_BASELINE_COLOR_CONTRAST_COLOR_PAIRS;
const [knownBgColor, knownFgColor] = knownBaselineColorPair.split('|');

test('a color-contrast node matching a known baseline bg/fg pair is baseline, regardless of its markup text', () => {
  const violations = [
    makeViolation('color-contrast', 'serious', [
      makeContrastNode('<button>Revoke enrollment</button>', knownBgColor, knownFgColor, [
        '.bg-destructive',
      ]),
    ]),
  ];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(1);
  expect(result.unexpected).toHaveLength(0);
});

test('a color-contrast node with a different bg/fg pair than any known baseline is unexpected', () => {
  const violations = [
    makeViolation('color-contrast', 'serious', [
      makeContrastNode('<button>Some new CTA</button>', '#123456', '#abcdef', ['.some-new-class']),
    ]),
  ];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(1);
});

test('a violation mixing a known color pair and an unknown color pair splits into both baseline and unexpected', () => {
  const knownNode = makeContrastNode(
    '<button>Revoke enrollment</button>',
    knownBgColor,
    knownFgColor,
    ['.bg-destructive'],
  );
  const unknownNode = makeContrastNode('<button>Some new CTA</button>', '#123456', '#abcdef', [
    '.some-new-class',
  ]);
  const violations = [makeViolation('color-contrast', 'serious', [knownNode, unknownNode])];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(1);
  expect(result.baseline[0]?.nodes).toHaveLength(1);
  expect(result.unexpected).toHaveLength(1);
  expect(result.unexpected[0]?.nodes).toHaveLength(1);
  expect(result.unexpected[0]?.nodes[0]?.target).toEqual(['.some-new-class']);
});
