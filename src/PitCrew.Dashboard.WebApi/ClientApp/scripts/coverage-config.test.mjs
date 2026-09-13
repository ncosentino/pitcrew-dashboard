import assert from 'node:assert/strict';
import { test } from 'node:test';

import config from '../vite.config.ts';

test('configures bounded production coverage output', () => {
  assert.equal(config.test.coverage.provider, 'v8');
  assert.deepEqual(config.test.coverage.reporter, ['json', 'json-summary']);
  assert.deepEqual(config.test.coverage.include, ['src/**/*.{ts,tsx}']);
  assert.equal(config.test.coverage.thresholds.perFile, true);

  for (const pattern of [
    'src/**/*.test.{ts,tsx}',
    'src/**/*.spec.{ts,tsx}',
    'src/**/*.d.ts',
    'src/setupTests.ts',
    'src/**/generated/**',
    'src/**/*.generated.{ts,tsx}',
  ]) {
    assert.ok(config.test.coverage.exclude.includes(pattern));
  }
});
