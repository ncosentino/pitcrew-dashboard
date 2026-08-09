/**
 * Pure-logic proof that axe classification is scoped to exact known nodes,
 * not whole rule IDs. Uses a hand-built `Result[]` (no live page/browser
 * needed — `test` callbacks that don't destructure `{ page }` skip that
 * fixture) so this runs fast and deterministically.
 */
import { test, expect } from '@playwright/test';
import type { CheckResult, NodeResult, Result } from 'axe-core';

import { classifyAxeResults } from './support/axe';

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

test('with an empty HTML baseline set, all color-contrast nodes are unexpected', () => {
  const violations = [
    makeViolation('color-contrast', 'serious', [
      makeNode('<div class="text-xs font-bold">Dashboard</div>'),
    ]),
  ];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(1);
});

test('a synthetic second color-contrast node that is not in the baseline is unexpected', () => {
  const syntheticUnknownNode = makeNode('<button class="bg-red-500 text-red-600">Danger</button>', [
    '.synthetic-unknown-node',
  ]);
  const violations = [makeViolation('color-contrast', 'serious', [syntheticUnknownNode])];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(1);
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

test('a non-color-contrast serious violation is never baseline', () => {
  const violations = [makeViolation('some-other-rule', 'serious', [makeNode('<div>test</div>')])];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(1);
});

test('moderate/minor impact violations never enter serious/critical classification', () => {
  const violations = [makeViolation('color-contrast', 'moderate', [makeNode('<div>test</div>')])];

  const result = classifyAxeResults(violations);

  expect(result.seriousOrCritical).toHaveLength(0);
  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(0);
});

test('with an empty color-pair baseline, a contrast node with any bg/fg pair is unexpected', () => {
  const violations = [
    makeViolation('color-contrast', 'serious', [
      makeContrastNode('<button>Revoke enrollment</button>', '#ff6467', '#ffffff', [
        '.bg-destructive',
      ]),
    ]),
  ];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(1);
});

test('a color-contrast node with an arbitrary bg/fg pair is unexpected when baselines are empty', () => {
  const violations = [
    makeViolation('color-contrast', 'serious', [
      makeContrastNode('<button>Some new CTA</button>', '#123456', '#abcdef', ['.some-new-class']),
    ]),
  ];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(1);
});

test('multiple unknown color-contrast nodes all classify as unexpected', () => {
  const nodeA = makeContrastNode('<button>Revoke enrollment</button>', '#ff6467', '#ffffff', [
    '.bg-destructive',
  ]);
  const nodeB = makeContrastNode('<button>Some new CTA</button>', '#123456', '#abcdef', [
    '.some-new-class',
  ]);
  const violations = [makeViolation('color-contrast', 'serious', [nodeA, nodeB])];

  const result = classifyAxeResults(violations);

  expect(result.baseline).toHaveLength(0);
  expect(result.unexpected).toHaveLength(1);
  expect(result.unexpected[0]?.nodes).toHaveLength(2);
});
