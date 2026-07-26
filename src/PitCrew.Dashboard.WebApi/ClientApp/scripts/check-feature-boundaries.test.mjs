import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, test } from 'node:test';

import { checkFeatureBoundaries } from './check-feature-boundaries.mjs';

const roots = [];

function fixture(files) {
  const root = mkdtempSync(join(tmpdir(), 'feature-boundaries-'));
  roots.push(root);
  for (const [path, content] of Object.entries(files)) {
    const fullPath = join(root, path);
    mkdirSync(join(fullPath, '..'), { recursive: true });
    writeFileSync(fullPath, content);
  }
  return root;
}

afterEach(() => {
  for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});

test('accepts imports within a feature and from core', () => {
  const root = fixture({
    'src/features/fleet/page.tsx':
      "import { api } from './api'; import { client } from '@/core/api/client';",
    'src/features/fleet/api.ts': 'export const api = true;',
  });

  assert.deepEqual(checkFeatureBoundaries(root), { scanned: 2, violations: [] });
});

test('reports static and dynamic sibling feature imports', () => {
  const root = fixture({
    'src/features/fleet/page.tsx':
      "import { settings } from '../settings/api'; void import('@/features/admin/page');",
    'src/features/settings/api.ts': 'export const settings = true;',
    'src/features/admin/page.tsx': 'export default function Page() { return null; }',
  });

  const result = checkFeatureBoundaries(root);

  assert.equal(result.scanned, 3);
  assert.deepEqual(
    result.violations.map(({ sourceFeature, targetFeature, specifier }) => ({
      sourceFeature,
      targetFeature,
      specifier,
    })),
    [
      {
        sourceFeature: 'fleet',
        targetFeature: 'settings',
        specifier: '../settings/api',
      },
      {
        sourceFeature: 'fleet',
        targetFeature: 'admin',
        specifier: '@/features/admin/page',
      },
    ],
  );
});
