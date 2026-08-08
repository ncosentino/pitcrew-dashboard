/**
 * Fixture schema-validation coverage. Every builder in `mocks/fixtures.ts`
 * parses its output through the same production zod schema the real API
 * clients validate responses with (`managerObservedStateSchema`,
 * `fleetNodeSchema`). These tests prove that claim is enforced rather than
 * aspirational: a positive case shows the shipped fixtures parse cleanly,
 * and a negative case shows a malformed override is rejected instead of
 * silently passing through, which would happen if the builders still used
 * an `as unknown` cast instead of `Schema.parse`.
 */
import { test, expect } from '@playwright/test';

import { buildFleetNode, buildProfile, nodeIds } from './mocks/fixtures';

test('buildProfile output satisfies managerObservedStateSchema', () => {
  expect(() => buildProfile('build')).not.toThrow();
});

test('buildProfile rejects an override that violates managerObservedStateSchema', () => {
  expect(() => buildProfile('build', { observedAt: 'not-a-date' })).toThrow();
});

test('buildFleetNode output satisfies fleetNodeSchema', () => {
  expect(() =>
    buildFleetNode({
      nodeId: nodeIds.alpha,
      displayName: 'Alpha',
      isOnline: true,
      profiles: [buildProfile('build')],
    }),
  ).not.toThrow();
});

test('buildFleetNode rejects a malformed nodeId instead of silently casting it', () => {
  expect(() =>
    buildFleetNode({
      nodeId: 'not-a-uuid',
      displayName: 'Alpha',
      isOnline: true,
      profiles: [],
    }),
  ).toThrow();
});
